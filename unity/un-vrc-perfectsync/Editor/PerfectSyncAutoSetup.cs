using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

internal enum PerfectSyncAutoSetupKind
{
	Direct,
	Derived,
}

internal sealed class PerfectSyncAutoSetupSuggestion
{
	public string KeyName = "";
	public string SourceName = "";
	public string Reason = "";
	public float Confidence;
	public float Coefficient = 1.0f;
	public bool GeometryValidated;
	public PerfectSyncAutoSetupKind Kind;
	public string Method = "";
	public bool OverwritesExisting;
	public BlendShapeDeltas? Deltas;
}

internal sealed class PerfectSyncAutoSetupPlan
{
	public List<PerfectSyncAutoSetupSuggestion> Suggestions { get; } = new();
	public int DirectCount => Suggestions.Count(suggestion => suggestion.Kind == PerfectSyncAutoSetupKind.Direct);
	public int DerivedCount => Suggestions.Count(suggestion => suggestion.Kind == PerfectSyncAutoSetupKind.Derived);
	public int OverwriteCount => Suggestions.Count(suggestion => suggestion.OverwritesExisting);
	public int GeometryValidatedCount => Suggestions.Count(suggestion => suggestion.GeometryValidated);
}

internal static class PerfectSyncAutoSetup
{
	private const float MinimumAutoConfidence = 0.72f;
	private const float MinimumFrameCoefficient = 0.25f;
	private const float MaximumFrameCoefficient = 4.0f;
	private const float MaximumPairReconstructionError = 0.35f;
	private const float MinimumSplitEnergyRatio = 0.02f;
	private const float MaximumOverlapScale = 1.25f;

	private enum Side
	{
		None,
		Left,
		Right,
	}

	private enum FaceRegion
	{
		None,
		Brow,
		Eye,
		MidFace,
		Mouth,
	}

	private sealed class PairRule
	{
		public string LeftKey = "";
		public string RightKey = "";
		public string[] Aliases = Array.Empty<string>();
		public string Section = "";
		public string[] SectionAliases = Array.Empty<string>();
	}

	private sealed class SourcePair
	{
		public SourceShape Left;
		public SourceShape Right;
		public int Score;
	}

	private sealed class RankedPair
	{
		public SourceShape Left;
		public SourceShape Right;
		public int Score;
		public float LeftCoefficient;
		public float RightCoefficient;
		public bool Usable;
	}

	private sealed class SingleRule
	{
		public string Key = "";
		public string[] Aliases = Array.Empty<string>();
		public string Section = "";
		public string[] SectionAliases = Array.Empty<string>();
		public bool MergePair;
	}

	private sealed class SourceShape
	{
		public string Name = "";
		public string CompactName = "";
		public string BaseName = "";
		public string Section = "";
		public Side Side;
		public int Index;
	}

	private readonly struct ShapeQuality
	{
		public ShapeQuality(bool usable, float score, float maxDisplacement, int affectedVertices)
		{
			Usable = usable;
			Score = score;
			MaxDisplacement = maxDisplacement;
			AffectedVertices = affectedVertices;
		}

		public bool Usable { get; }
		public float Score { get; }
		public float MaxDisplacement { get; }
		public int AffectedVertices { get; }
	}

	private sealed class GeometryContext
	{
		public bool Reliable;
		public Vector3[] Vertices = Array.Empty<Vector3>();
		public Vector3 EyeCenter;
		public Vector3 HorizontalAxis;
		public Vector3 UpAxis;
		public float EyeDistance;
	}

	private readonly struct ShapeGeometry
	{
		public ShapeGeometry(
			float leftRatio,
			float rightRatio,
			float browRatio,
			float eyeRatio,
			float midFaceRatio,
			float mouthRatio)
		{
			LeftRatio = leftRatio;
			RightRatio = rightRatio;
			BrowRatio = browRatio;
			EyeRatio = eyeRatio;
			MidFaceRatio = midFaceRatio;
			MouthRatio = mouthRatio;
		}

		public float LeftRatio { get; }
		public float RightRatio { get; }
		public float BrowRatio { get; }
		public float EyeRatio { get; }
		public float MidFaceRatio { get; }
		public float MouthRatio { get; }
	}

	private readonly struct GeometryEvidence
	{
		public GeometryEvidence(bool usable, float score, string summary)
		{
			Usable = usable;
			Score = score;
			Summary = summary;
		}

		public bool Usable { get; }
		public float Score { get; }
		public string Summary { get; }
	}

	private static readonly PairRule[] PairRules =
	{
		Pair("EyeLookUpLeft", "EyeLookUpRight", "eyelookup", "lookingup", "lookupeye", "look_up"),
		Pair("EyeLookDownLeft", "EyeLookDownRight", "eyelookdown", "lookingdown", "lookdowneye", "look_down"),
		Pair("EyeLookInLeft", "EyeLookInRight", "eyelookin", "lookingin", "lookineye", "eyemovenarrow", "moveyori", "look_in"),
		Pair("EyeLookOutLeft", "EyeLookOutRight", "eyelookout", "lookingout", "lookouteye", "eyemovewide", "movesori", "look_out"),
		PairInSection("EyeBlinkLeft", "EyeBlinkRight", "eye", new[] { "close", "closed" }, "eyeblink", "eyeclosed", "eyesclosed", "fcleyeclose", "blink", "mabataki", "まばたき"),
		PairInSection("EyeSquintLeft", "EyeSquintRight", "eye", new[] { "lowerlidup", "lowerup", "smile", "nagomi" }, "eyesquint", "eyesmile", "smileeye", "eyelowerlidup", "lowerlidup", "fcleyejoy", "nagomi", "にこ", "笑い目"),
		PairInSection("EyeWideLeft", "EyeWideRight", "eye", new[] { "open", "wide" }, "eyewide", "eyeopen", "openeye", "wideeye", "fcleyesurprised", "見開"),
		PairInSection("BrowDownLeft", "BrowDownRight", "brow", new[] { "down" }, "browdown", "eyebrowdown", "mayudown", "fclbrwangry", "眉下"),
		PairInSection("BrowOuterUpLeft", "BrowOuterUpRight", "brow", new[] { "up" }, "browouterup", "outerbrowup", "eyebrowup", "browup", "fclbrwsurprised", "眉上"),
		Pair("CheekSquintLeft", "CheekSquintRight", "cheeksquint", "cheekraise", "cheekup", "頬上", "頬あげ"),
		Pair("NoseSneerLeft", "NoseSneerRight", "nosesneer", "nosewrinkle", "鼻しかめ"),
		Pair("MouthSmileLeft", "MouthSmileRight", "mouthsmile", "smilemouth", "fclmthjoy", "kuchismile", "口角上"),
		Pair("MouthFrownLeft", "MouthFrownRight", "mouthfrown", "frownmouth", "mouthsad", "fclmthsorrow", "口角下"),
		Pair("MouthDimpleLeft", "MouthDimpleRight", "mouthdimple", "dimple", "grin", "口角横"),
		Pair("MouthUpperUpLeft", "MouthUpperUpRight", "mouthupperup", "upperlipup", "lipupperup", "上唇上"),
		Pair("MouthLowerDownLeft", "MouthLowerDownRight", "mouthlowerdown", "lowerlipdown", "liplowerdown", "下唇下"),
		Pair("MouthPressLeft", "MouthPressRight", "mouthpress", "lippress", "唇押"),
		Pair("MouthStretchLeft", "MouthStretchRight", "mouthstretch", "lipstretch", "mouthwide", "口横"),
	};

	private static readonly SingleRule[] SingleRules =
	{
		SinglePairInSection("BrowInnerUp", "brow", new[] { "sad", "trouble", "komaru" }, "browinnerup", "innerbrowup", "browcenterup", "browsad", "eyebrowsad", "browtrouble", "eyebrowtrouble", "fclbrwsorrow", "眉内上"),
		SinglePair("CheekPuff", "cheekpuff", "puffcheek", "cheekpuku", "facepuku", "puku", "ふくれ", "頬膨"),
		Single("JawOpen", "jawopen", "mouthopen", "openmouth", "vrcvaa", "visemeaa", "fclmtha"),
		Single("JawForward", "jawforward", "jawfront", "顎前"),
		Single("JawLeft", "jawleft", "顎左"),
		Single("JawRight", "jawright", "顎右"),
		Single("MouthFunnel", "mouthfunnel", "funnel", "vrcvoh", "visemeoh", "fclmtho"),
		Single("MouthPucker", "mouthpucker", "pucker", "vrcvou", "visemeou", "fclmthu"),
		Single("MouthLeft", "mouthleft", "口左"),
		Single("MouthRight", "mouthright", "口右"),
		Single("MouthRollUpper", "mouthrollupper", "upperliproll", "上唇巻"),
		Single("MouthRollLower", "mouthrolllower", "lowerliproll", "下唇巻"),
		Single("MouthShrugUpper", "mouthshrugupper", "uppershrug", "upperlipraise", "上唇上げ"),
		Single("MouthShrugLower", "mouthshruglower", "lowershrug", "lowerlipraise", "下唇上げ"),
		Single("MouthClose", "mouthclose", "closemouth", "vrcvpp", "visemepp"),
		SingleInSection("TongueOut", "tongue", new[] { "tongue1", "tang1" }, "tongueout", "mouthtongue", "tongue", "tang1", "舌出"),
	};

	public static PerfectSyncAutoSetupPlan Analyze(
		SkinnedMeshRenderer renderer,
		IReadOnlyDictionary<string, PerfectSyncKeyState> states,
		bool overwriteConfigured = false)
	{
		var plan = new PerfectSyncAutoSetupPlan();
		var mesh = renderer != null ? renderer.sharedMesh : null;
		if (mesh == null)
		{
			return plan;
		}

		var canonicalNames = new HashSet<string>(PerfectSyncKeys.Names, StringComparer.OrdinalIgnoreCase);
		var sources = CreateSources(mesh, canonicalNames);
		var descriptorHints = FindDescriptorHints(renderer, sources);
		var reservedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var deltaCache = new Dictionary<int, BlendShapeDeltas>();
		var geometry = CreateGeometryContext(renderer, mesh);

		foreach (var rule in PairRules)
		{
			AnalyzePair(renderer, mesh, states, sources, descriptorHints, reservedSources, deltaCache, geometry, plan, rule, overwriteConfigured);
		}

		foreach (var rule in SingleRules)
		{
			if (!CanConfigure(states, rule.Key, overwriteConfigured))
			{
				continue;
			}

			descriptorHints.TryGetValue(rule.Key, out var descriptorMatch);
			var match = descriptorMatch != null && !reservedSources.Contains(descriptorMatch.Name)
				? descriptorMatch
				: BestSingleMatch(sources, rule, reservedSources);
			if (match != null && TryGetDirectCoefficient(mesh, match, deltaCache, out var coefficient, out var quality))
			{
				var geometryEvidence = EvaluateGeometry(geometry, GetDeltas(mesh, match.Index, deltaCache), rule.Key);
				if (!geometryEvidence.Usable)
				{
					continue;
				}

				var confidence = (descriptorMatch == match ? 0.98f : MatchConfidence(match, rule))
					* quality.Score
					* geometryEvidence.Score;
				if (confidence < MinimumAutoConfidence)
				{
					continue;
				}

				reservedSources.Add(match.Name);
				plan.Suggestions.Add(new PerfectSyncAutoSetupSuggestion
				{
					KeyName = rule.Key,
					SourceName = match.Name,
					Kind = PerfectSyncAutoSetupKind.Direct,
					Confidence = confidence,
					Coefficient = coefficient,
					GeometryValidated = !string.IsNullOrEmpty(geometryEvidence.Summary),
					Reason = (descriptorMatch == match
						? $"VRC Avatar Descriptor: {match.Name}"
						: $"名前が一致: {match.Name}") + GeometryReason(geometryEvidence),
				});
				continue;
			}

			var pair = rule.MergePair ? BestSinglePair(sources, rule, reservedSources) : null;
			if (pair == null)
			{
				continue;
			}

			if (!TryCreateSafeMerge(mesh, pair.Left, pair.Right, deltaCache, out var merged, out var mergeQuality))
			{
				continue;
			}

			var mergeGeometry = EvaluateGeometry(geometry, merged, rule.Key);
			if (!mergeGeometry.Usable)
			{
				continue;
			}

			var mergeConfidence = Mathf.Clamp01(0.70f + pair.Score * 0.002f)
				* mergeQuality.Score
				* mergeGeometry.Score;
			if (mergeConfidence < MinimumAutoConfidence)
			{
				continue;
			}

			reservedSources.Add(pair.Left.Name);
			reservedSources.Add(pair.Right.Name);
			plan.Suggestions.Add(new PerfectSyncAutoSetupSuggestion
			{
				KeyName = rule.Key,
				SourceName = $"{pair.Left.Name} + {pair.Right.Name}",
				Kind = PerfectSyncAutoSetupKind.Derived,
				Method = "左右合成",
				Confidence = mergeConfidence,
				GeometryValidated = !string.IsNullOrEmpty(mergeGeometry.Summary),
				Reason = $"左右ペアを合成: {pair.Left.Name}, {pair.Right.Name}" + GeometryReason(mergeGeometry),
				Deltas = merged,
			});
		}

		foreach (var suggestion in plan.Suggestions)
		{
			suggestion.OverwritesExisting = IsConfigured(states[suggestion.KeyName]);
		}

		return plan;
	}

	private static void AnalyzePair(
		SkinnedMeshRenderer renderer,
		Mesh mesh,
		IReadOnlyDictionary<string, PerfectSyncKeyState> states,
		IReadOnlyList<SourceShape> sources,
		IReadOnlyDictionary<string, SourceShape> descriptorHints,
		ISet<string> reservedSources,
		IDictionary<int, BlendShapeDeltas> deltaCache,
		GeometryContext geometry,
		PerfectSyncAutoSetupPlan plan,
		PairRule rule,
		bool overwriteConfigured)
	{
		var needsLeft = CanConfigure(states, rule.LeftKey, overwriteConfigured);
		var needsRight = CanConfigure(states, rule.RightKey, overwriteConfigured);
		if (!needsLeft && !needsRight)
		{
			return;
		}

		var candidates = sources
			.Where(source => source.Side != Side.None && PairAliasScore(source, rule, true) > 0)
			.GroupBy(source => new { source.BaseName, source.Section })
			.Select(group => new
			{
				Left = group.FirstOrDefault(source => source.Side == Side.Left),
				Right = group.FirstOrDefault(source => source.Side == Side.Right),
				Score = PairAliasScore(group.First(), rule, true),
			})
			.Where(candidate => candidate.Left != null && candidate.Right != null)
			.Where(candidate => !reservedSources.Contains(candidate.Left.Name) && !reservedSources.Contains(candidate.Right.Name))
			.ToList();

		descriptorHints.TryGetValue(rule.LeftKey, out var descriptorLeft);
		descriptorHints.TryGetValue(rule.RightKey, out var descriptorRight);
		var descriptorCombined = descriptorLeft != null && descriptorLeft == descriptorRight
			? descriptorLeft
			: null;
		var combined = descriptorCombined != null && !reservedSources.Contains(descriptorCombined.Name)
			? descriptorCombined
			: sources
			.Where(source => source.Side == Side.None && !reservedSources.Contains(source.Name))
			.Where(source => PairAliasScore(source, rule, false) > 0)
			.OrderByDescending(source => CombinedSourceScore(source, rule))
			.FirstOrDefault();

		if (candidates.Count > 0)
		{
			var ranked = candidates
				.Where(candidate => TryGetDirectCoefficient(mesh, candidate.Left, deltaCache, out _, out _)
					&& TryGetDirectCoefficient(mesh, candidate.Right, deltaCache, out _, out _))
				.Select(candidate => RankPair(
					mesh,
					candidate.Left,
					candidate.Right,
					combined,
					candidate.Score,
					deltaCache,
					geometry,
					rule.LeftKey,
					rule.RightKey))
				.Where(candidate => candidate.Usable)
				.OrderByDescending(candidate => candidate.Score)
				.FirstOrDefault();
			if (ranked != null && needsLeft)
			{
				AddDirect(mesh, deltaCache, geometry, plan, rule.LeftKey, ranked.Left, ranked.Score, "左右ペアを検出", ranked.LeftCoefficient);
			}

			if (ranked != null && needsRight)
			{
				AddDirect(mesh, deltaCache, geometry, plan, rule.RightKey, ranked.Right, ranked.Score, "左右ペアを検出", ranked.RightCoefficient);
			}

			if (ranked != null)
			{
				reservedSources.Add(ranked.Left.Name);
				reservedSources.Add(ranked.Right.Name);
				return;
			}
		}

		if (!needsLeft || !needsRight || combined == null)
		{
			return;
		}

		if (!TryGetDirectCoefficient(mesh, combined, deltaCache, out var combinedCoefficient, out var combinedQuality))
		{
			return;
		}

		var combinedDeltas = ScaleDeltas(GetDeltas(mesh, combined.Index, deltaCache), combinedCoefficient);
		var split = SplitLeftRight(renderer, mesh, combinedDeltas);
		var leftQuality = EvaluateDeltas(mesh, split.Left, 1.0f);
		var rightQuality = EvaluateDeltas(mesh, split.Right, 1.0f);
		var leftGeometry = EvaluateGeometry(geometry, split.Left, rule.LeftKey);
		var rightGeometry = EvaluateGeometry(geometry, split.Right, rule.RightKey);
		var combinedEnergy = VertexEnergy(combinedDeltas);
		var leftEnergyRatio = combinedEnergy > 1e-16 ? VertexEnergy(split.Left) / combinedEnergy : 0.0;
		var rightEnergyRatio = combinedEnergy > 1e-16 ? VertexEnergy(split.Right) / combinedEnergy : 0.0;
		if (!leftQuality.Usable
			|| !rightQuality.Usable
			|| !leftGeometry.Usable
			|| !rightGeometry.Usable
			|| leftEnergyRatio < MinimumSplitEnergyRatio
			|| rightEnergyRatio < MinimumSplitEnergyRatio)
		{
			return;
		}

		var splitConfidence = (descriptorCombined == combined ? 0.94f : 0.76f)
			* Mathf.Min(combinedQuality.Score, leftQuality.Score, rightQuality.Score)
			* Mathf.Min(leftGeometry.Score, rightGeometry.Score);
		if (splitConfidence < MinimumAutoConfidence)
		{
			return;
		}

		plan.Suggestions.Add(new PerfectSyncAutoSetupSuggestion
		{
			KeyName = rule.LeftKey,
			SourceName = combined.Name,
			Kind = PerfectSyncAutoSetupKind.Derived,
			Method = "左右分割",
			Confidence = splitConfidence,
			GeometryValidated = !string.IsNullOrEmpty(leftGeometry.Summary),
			Reason = (descriptorCombined == combined
				? $"VRC Avatar Descriptor の {combined.Name} を左右に分割"
				: $"{combined.Name} を左右に分割") + GeometryReason(leftGeometry),
			Deltas = split.Left,
		});
		plan.Suggestions.Add(new PerfectSyncAutoSetupSuggestion
		{
			KeyName = rule.RightKey,
			SourceName = combined.Name,
			Kind = PerfectSyncAutoSetupKind.Derived,
			Method = "左右分割",
			Confidence = splitConfidence,
			GeometryValidated = !string.IsNullOrEmpty(rightGeometry.Summary),
			Reason = (descriptorCombined == combined
				? $"VRC Avatar Descriptor の {combined.Name} を左右に分割"
				: $"{combined.Name} を左右に分割") + GeometryReason(rightGeometry),
			Deltas = split.Right,
		});
		reservedSources.Add(combined.Name);
	}

	private static bool CanConfigure(
		IReadOnlyDictionary<string, PerfectSyncKeyState> states,
		string keyName,
		bool overwriteConfigured)
	{
		if (!states.TryGetValue(keyName, out var state))
		{
			return false;
		}

		return overwriteConfigured
			? !IsCaseMismatch(state)
			: !IsConfigured(state);
	}

	private static bool IsCaseMismatch(PerfectSyncKeyState state)
	{
		return state.status == PerfectSyncKeyStatus.Warning
			&& !string.IsNullOrEmpty(state.meshBlendShapeName);
	}

	private static bool IsConfigured(PerfectSyncKeyState state)
	{
		return state.sources.Any(source => source.enabled)
			|| state.status is not (PerfectSyncKeyStatus.Undefined or PerfectSyncKeyStatus.Empty);
	}

	private static IReadOnlyDictionary<string, SourceShape> FindDescriptorHints(
		SkinnedMeshRenderer renderer,
		IReadOnlyList<SourceShape> sources)
	{
		var hints = new Dictionary<string, SourceShape>(StringComparer.Ordinal);
		var descriptor = renderer
			.GetComponentsInParent<Component>(true)
			.FirstOrDefault(component => component != null
				&& component.GetType().FullName == "VRC.SDKBase.VRC_AvatarDescriptor");
		if (descriptor == null)
		{
			return hints;
		}

		var byName = sources
			.GroupBy(source => source.Name, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		var serialized = new SerializedObject(descriptor);
		AddVisemeHints(descriptor.GetType(), serialized, renderer, byName, hints);
		AddEyelidHints(serialized, renderer, sources, hints);
		return hints;
	}

	private static void AddVisemeHints(
		Type descriptorType,
		SerializedObject descriptor,
		SkinnedMeshRenderer renderer,
		IReadOnlyDictionary<string, SourceShape> sources,
		IDictionary<string, SourceShape> hints)
	{
		var visemeRenderer = descriptor.FindProperty("VisemeSkinnedMesh")?.objectReferenceValue;
		var visemes = descriptor.FindProperty("VisemeBlendShapes");
		var visemeType = descriptorType.GetNestedType("Viseme");
		if (visemeRenderer != renderer || visemes == null || !visemes.isArray || visemeType == null)
		{
			return;
		}

		var names = Enum.GetNames(visemeType);
		AddVisemeHint("JawOpen", "aa");
		AddVisemeHint("MouthClose", "PP");
		AddVisemeHint("MouthFunnel", "oh");
		AddVisemeHint("MouthPucker", "ou");

		void AddVisemeHint(string keyName, string visemeName)
		{
			var index = Array.FindIndex(names, name => string.Equals(name, visemeName, StringComparison.OrdinalIgnoreCase));
			if (index < 0 || index >= visemes.arraySize)
			{
				return;
			}

			var sourceName = visemes.GetArrayElementAtIndex(index).stringValue;
			if (!string.IsNullOrEmpty(sourceName) && sources.TryGetValue(sourceName, out var source))
			{
				hints[keyName] = source;
			}
		}
	}

	private static void AddEyelidHints(
		SerializedObject descriptor,
		SkinnedMeshRenderer renderer,
		IReadOnlyList<SourceShape> sources,
		IDictionary<string, SourceShape> hints)
	{
		var eyeSettings = descriptor.FindProperty("customEyeLookSettings");
		var eyelidRenderer = eyeSettings?.FindPropertyRelative("eyelidsSkinnedMesh")?.objectReferenceValue;
		var indices = eyeSettings?.FindPropertyRelative("eyelidsBlendshapes");
		if (eyelidRenderer != renderer || indices == null || !indices.isArray)
		{
			return;
		}

		AddPairHint("EyeBlinkLeft", "EyeBlinkRight", 0);
		AddPairHint("EyeLookUpLeft", "EyeLookUpRight", 1);
		AddPairHint("EyeLookDownLeft", "EyeLookDownRight", 2);

		void AddPairHint(string leftKey, string rightKey, int descriptorIndex)
		{
			if (descriptorIndex >= indices.arraySize)
			{
				return;
			}

			var blendShapeIndex = indices.GetArrayElementAtIndex(descriptorIndex).intValue;
			var source = sources.FirstOrDefault(candidate => candidate.Index == blendShapeIndex);
			if (source != null)
			{
				hints[leftKey] = source;
				hints[rightKey] = source;
			}
		}
	}

	private static void AddDirect(
		Mesh mesh,
		IDictionary<int, BlendShapeDeltas> deltaCache,
		GeometryContext geometry,
		PerfectSyncAutoSetupPlan plan,
		string keyName,
		SourceShape source,
		float score,
		string reason,
		float? coefficientOverride = null)
	{
		if (!TryGetDirectCoefficient(mesh, source, deltaCache, out var frameCoefficient, out _))
		{
			return;
		}

		var coefficient = coefficientOverride ?? frameCoefficient;
		var quality = EvaluateDeltas(mesh, GetDeltas(mesh, source.Index, deltaCache), coefficient);
		if (!quality.Usable)
		{
			return;
		}

		var geometryEvidence = EvaluateGeometry(geometry, GetDeltas(mesh, source.Index, deltaCache), keyName);
		if (!geometryEvidence.Usable)
		{
			return;
		}

		var confidence = Mathf.Clamp01(0.72f + score * 0.002f)
			* quality.Score
			* geometryEvidence.Score;
		if (confidence < MinimumAutoConfidence)
		{
			return;
		}

		plan.Suggestions.Add(new PerfectSyncAutoSetupSuggestion
		{
			KeyName = keyName,
			SourceName = source.Name,
			Kind = PerfectSyncAutoSetupKind.Direct,
			Confidence = confidence,
			Coefficient = coefficient,
			GeometryValidated = !string.IsNullOrEmpty(geometryEvidence.Summary),
			Reason = $"{reason}: {source.Name}" + GeometryReason(geometryEvidence),
		});
	}

	private static SourceShape BestSingleMatch(
		IEnumerable<SourceShape> sources,
		SingleRule rule,
		ISet<string> reservedSources)
	{
		return sources
			.Where(source => source.Side == Side.None && !reservedSources.Contains(source.Name))
			.Select(source => new { Source = source, Score = SingleAliasScore(source, rule) })
			.Where(candidate => candidate.Score >= 80)
			.OrderByDescending(candidate => candidate.Score)
			.ThenBy(candidate => candidate.Source.Name.Length)
			.Select(candidate => candidate.Source)
			.FirstOrDefault();
	}

	private static SourcePair BestSinglePair(
		IEnumerable<SourceShape> sources,
		SingleRule rule,
		ISet<string> reservedSources)
	{
		return sources
			.Where(source => source.Side != Side.None && !reservedSources.Contains(source.Name))
			.GroupBy(source => new { source.BaseName, source.Section })
			.Select(group => new SourcePair
			{
				Left = group.FirstOrDefault(source => source.Side == Side.Left),
				Right = group.FirstOrDefault(source => source.Side == Side.Right),
				Score = SingleAliasScore(group.First(), rule, true),
			})
			.Where(pair => pair.Left != null && pair.Right != null && pair.Score >= 80)
			.OrderByDescending(pair => pair.Score)
			.ThenBy(pair => pair.Left.Name.Length + pair.Right.Name.Length)
			.FirstOrDefault();
	}

	private static float MatchConfidence(SourceShape source, SingleRule rule)
	{
		return Mathf.Clamp01(0.65f + SingleAliasScore(source, rule) * 0.0035f);
	}

	private static int CombinedSourceScore(SourceShape source, PairRule rule)
	{
		var score = PairAliasScore(source, rule, false);
		if (source.CompactName.StartsWith("vrc", StringComparison.Ordinal))
		{
			score += 25;
		}

		return score;
	}

	private static int PairAliasScore(SourceShape source, PairRule rule, bool useBaseName)
	{
		var name = useBaseName ? source.BaseName : source.CompactName;
		var score = AliasScore(name, rule.Aliases);
		if (source.Section == rule.Section)
		{
			score = Math.Max(score, AliasScore(name, rule.SectionAliases));
		}

		return score;
	}

	private static int SingleAliasScore(SourceShape source, SingleRule rule, bool useBaseName = false)
	{
		var name = useBaseName ? source.BaseName : source.CompactName;
		var score = AliasScore(name, rule.Aliases);
		if (source.Section == rule.Section)
		{
			score = Math.Max(score, AliasScore(name, rule.SectionAliases));
		}

		return score;
	}

	private static bool TryCreateSafeMerge(
		Mesh mesh,
		SourceShape leftSource,
		SourceShape rightSource,
		IDictionary<int, BlendShapeDeltas> deltaCache,
		out BlendShapeDeltas merged,
		out ShapeQuality quality)
	{
		merged = default;
		quality = default;
		if (!TryGetDirectCoefficient(mesh, leftSource, deltaCache, out var leftCoefficient, out _)
			|| !TryGetDirectCoefficient(mesh, rightSource, deltaCache, out var rightCoefficient, out _))
		{
			return false;
		}

		var left = ScaleDeltas(GetDeltas(mesh, leftSource.Index, deltaCache), leftCoefficient);
		var right = ScaleDeltas(GetDeltas(mesh, rightSource.Index, deltaCache), rightCoefficient);
		var vertexCount = left.Vertices.Length;
		var vertices = new Vector3[vertexCount];
		var normals = new Vector3[vertexCount];
		var tangents = new Vector3[vertexCount];
		for (var index = 0; index < vertexCount; index++)
		{
			var vertex = left.Vertices[index] + right.Vertices[index];
			var maximumMagnitude = Mathf.Max(left.Vertices[index].magnitude, right.Vertices[index].magnitude)
				* MaximumOverlapScale;
			var scale = maximumMagnitude > 0.0f && vertex.magnitude > maximumMagnitude
				? maximumMagnitude / vertex.magnitude
				: 1.0f;
			vertices[index] = vertex * scale;
			normals[index] = (left.Normals[index] + right.Normals[index]) * scale;
			tangents[index] = (left.Tangents[index] + right.Tangents[index]) * scale;
		}

		merged = new BlendShapeDeltas(vertices, normals, tangents);
		quality = EvaluateDeltas(mesh, merged, 1.0f);
		return quality.Usable;
	}

	private static BlendShapeDeltas ScaleDeltas(BlendShapeDeltas source, float coefficient)
	{
		var vertexCount = source.Vertices.Length;
		var vertices = new Vector3[vertexCount];
		var normals = new Vector3[vertexCount];
		var tangents = new Vector3[vertexCount];
		for (var index = 0; index < vertexCount; index++)
		{
			vertices[index] = source.Vertices[index] * coefficient;
			normals[index] = source.Normals[index] * coefficient;
			tangents[index] = source.Tangents[index] * coefficient;
		}

		return new BlendShapeDeltas(vertices, normals, tangents);
	}

	private static bool TryGetDirectCoefficient(
		Mesh mesh,
		SourceShape source,
		IDictionary<int, BlendShapeDeltas> deltaCache,
		out float coefficient,
		out ShapeQuality quality)
	{
		coefficient = 1.0f;
		quality = default;
		var frameCount = mesh.GetBlendShapeFrameCount(source.Index);
		if (frameCount <= 0)
		{
			return false;
		}

		var frameWeight = mesh.GetBlendShapeFrameWeight(source.Index, frameCount - 1);
		if (!IsFinite(frameWeight) || frameWeight <= 0.0001f)
		{
			return false;
		}

		coefficient = 100.0f / frameWeight;
		if (!IsFinite(coefficient)
			|| coefficient < MinimumFrameCoefficient
			|| coefficient > MaximumFrameCoefficient)
		{
			return false;
		}

		quality = EvaluateDeltas(mesh, GetDeltas(mesh, source.Index, deltaCache), coefficient);
		return quality.Usable;
	}

	private static ShapeQuality EvaluateDeltas(Mesh mesh, BlendShapeDeltas deltas, float coefficient)
	{
		if (deltas.Vertices == null
			|| deltas.Normals == null
			|| deltas.Tangents == null
			|| deltas.Vertices.Length != mesh.vertexCount
			|| deltas.Normals.Length != mesh.vertexCount
			|| deltas.Tangents.Length != mesh.vertexCount
			|| !IsFinite(coefficient))
		{
			return default;
		}

		var meshScale = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z, 0.0001f);
		var movementThreshold = meshScale * 0.000001f;
		var maximumAllowed = meshScale * 2.0f;
		var affectedVertices = 0;
		var maxDisplacement = 0.0f;
		double energy = 0.0;
		for (var index = 0; index < mesh.vertexCount; index++)
		{
			var vertex = deltas.Vertices[index] * coefficient;
			var normal = deltas.Normals[index] * coefficient;
			var tangent = deltas.Tangents[index] * coefficient;
			if (!IsFinite(vertex) || !IsFinite(normal) || !IsFinite(tangent))
			{
				return default;
			}

			var magnitude = vertex.magnitude;
			if (magnitude <= movementThreshold)
			{
				continue;
			}

			affectedVertices++;
			maxDisplacement = Mathf.Max(maxDisplacement, magnitude);
			energy += vertex.sqrMagnitude;
		}

		if (affectedVertices == 0 || maxDisplacement > maximumAllowed || energy <= 1e-16)
		{
			return new ShapeQuality(false, 0.0f, maxDisplacement, affectedVertices);
		}

		var displacementRatio = maxDisplacement / meshScale;
		var score = displacementRatio <= 0.5f
			? 1.0f
			: Mathf.Lerp(1.0f, 0.65f, Mathf.InverseLerp(0.5f, 2.0f, displacementRatio));
		return new ShapeQuality(true, score, maxDisplacement, affectedVertices);
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	private static bool IsFinite(Vector3 value)
	{
		return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
	}

	private static GeometryContext CreateGeometryContext(SkinnedMeshRenderer renderer, Mesh mesh)
	{
		var context = new GeometryContext { Vertices = mesh.vertices };
		var animator = renderer.GetComponentInParent<Animator>();
		if (animator == null || !animator.isHuman)
		{
			return context;
		}

		var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
		var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
		if (leftEye == null || rightEye == null)
		{
			return context;
		}

		var leftPosition = renderer.transform.InverseTransformPoint(leftEye.position);
		var rightPosition = renderer.transform.InverseTransformPoint(rightEye.position);
		var horizontal = rightPosition - leftPosition;
		var eyeDistance = horizontal.magnitude;
		if (!IsFinite(horizontal) || eyeDistance <= 0.00001f)
		{
			return context;
		}

		horizontal /= eyeDistance;
		var up = renderer.transform.InverseTransformDirection(animator.transform.up);
		up -= horizontal * Vector3.Dot(up, horizontal);
		if (!IsFinite(up) || up.sqrMagnitude <= 0.000001f)
		{
			return context;
		}

		context.Reliable = true;
		context.EyeCenter = (leftPosition + rightPosition) * 0.5f;
		context.HorizontalAxis = horizontal;
		context.UpAxis = up.normalized;
		context.EyeDistance = eyeDistance;
		return context;
	}

	private static GeometryEvidence EvaluateGeometry(
		GeometryContext context,
		BlendShapeDeltas deltas,
		string keyName)
	{
		if (!context.Reliable)
		{
			return new GeometryEvidence(true, 1.0f, "");
		}

		var geometry = AnalyzeShapeGeometry(context, deltas);
		var expectedRegion = RegionForKey(keyName);
		var regionRatio = RegionRatio(geometry, expectedRegion);
		var expectedSide = SideForKey(keyName);
		var sideRatio = expectedSide == Side.Left
			? geometry.LeftRatio
			: expectedSide == Side.Right
				? geometry.RightRatio
				: 1.0f;
		if ((expectedRegion != FaceRegion.None && regionRatio < 0.05f)
			|| (expectedSide != Side.None && sideRatio < 0.20f))
		{
			return new GeometryEvidence(false, 0.0f, "");
		}

		var score = 1.0f;
		if (expectedRegion != FaceRegion.None)
		{
			score *= Mathf.Lerp(0.94f, 1.0f, regionRatio);
		}

		if (expectedSide != Side.None)
		{
			score *= Mathf.Lerp(0.94f, 1.0f, sideRatio);
		}

		var regionText = expectedRegion == FaceRegion.None ? "" : $"領域 {regionRatio:P0}";
		var sideText = expectedSide == Side.None ? "" : $"{(expectedSide == Side.Left ? "左側" : "右側")} {sideRatio:P0}";
		var summary = string.Join("、", new[] { regionText, sideText }.Where(value => !string.IsNullOrEmpty(value)));
		return new GeometryEvidence(true, score, summary);
	}

	private static ShapeGeometry AnalyzeShapeGeometry(GeometryContext context, BlendShapeDeltas deltas)
	{
		double total = 0.0;
		double left = 0.0;
		double right = 0.0;
		double brow = 0.0;
		double eye = 0.0;
		double midFace = 0.0;
		double mouth = 0.0;
		var sideBlendWidth = context.EyeDistance * 0.08f;
		for (var index = 0; index < deltas.Vertices.Length; index++)
		{
			var energy = deltas.Vertices[index].sqrMagnitude;
			if (energy <= 1e-16f)
			{
				continue;
			}

			var relative = context.Vertices[index] - context.EyeCenter;
			var horizontal = Vector3.Dot(relative, context.HorizontalAxis);
			var vertical = Vector3.Dot(relative, context.UpAxis) / context.EyeDistance;
			var rightWeight = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(-sideBlendWidth, sideBlendWidth, horizontal));
			var leftWeight = 1.0f - rightWeight;
			total += energy;
			left += energy * leftWeight;
			right += energy * rightWeight;
			brow += energy * BandWeight(vertical, 0.10f, 1.00f, 0.20f);
			eye += energy * BandWeight(vertical, -0.35f, 0.35f, 0.15f);
			midFace += energy * BandWeight(vertical, -1.40f, -0.05f, 0.25f);
			mouth += energy * BandWeight(vertical, -3.00f, -0.20f, 0.20f);
		}

		if (total <= 1e-16)
		{
			return default;
		}

		return new ShapeGeometry(
			(float)(left / total),
			(float)(right / total),
			(float)(brow / total),
			(float)(eye / total),
			(float)(midFace / total),
			(float)(mouth / total));
	}

	private static float BandWeight(float value, float minimum, float maximum, float fade)
	{
		if (value < minimum)
		{
			return Mathf.InverseLerp(minimum - fade, minimum, value);
		}

		if (value > maximum)
		{
			return 1.0f - Mathf.InverseLerp(maximum, maximum + fade, value);
		}

		return 1.0f;
	}

	private static FaceRegion RegionForKey(string keyName)
	{
		if (keyName.StartsWith("Brow", StringComparison.Ordinal))
		{
			return FaceRegion.Brow;
		}

		if (keyName.StartsWith("Eye", StringComparison.Ordinal))
		{
			return FaceRegion.Eye;
		}

		if (keyName.StartsWith("Cheek", StringComparison.Ordinal)
			|| keyName.StartsWith("Nose", StringComparison.Ordinal))
		{
			return FaceRegion.MidFace;
		}

		if (keyName.StartsWith("Mouth", StringComparison.Ordinal)
			|| keyName.StartsWith("Jaw", StringComparison.Ordinal)
			|| keyName.StartsWith("Tongue", StringComparison.Ordinal))
		{
			return FaceRegion.Mouth;
		}

		return FaceRegion.None;
	}

	private static float RegionRatio(ShapeGeometry geometry, FaceRegion region)
	{
		return region switch
		{
			FaceRegion.Brow => geometry.BrowRatio,
			FaceRegion.Eye => geometry.EyeRatio,
			FaceRegion.MidFace => geometry.MidFaceRatio,
			FaceRegion.Mouth => geometry.MouthRatio,
			_ => 1.0f,
		};
	}

	private static Side SideForKey(string keyName)
	{
		if (keyName is "JawLeft" or "JawRight" or "MouthLeft" or "MouthRight")
		{
			return Side.None;
		}

		if (keyName.EndsWith("Left", StringComparison.Ordinal))
		{
			return Side.Left;
		}

		return keyName.EndsWith("Right", StringComparison.Ordinal) ? Side.Right : Side.None;
	}

	private static string GeometryReason(GeometryEvidence evidence)
	{
		return string.IsNullOrEmpty(evidence.Summary) ? "" : $"（幾何: {evidence.Summary}）";
	}

	private static RankedPair RankPair(
		Mesh mesh,
		SourceShape left,
		SourceShape right,
		SourceShape combined,
		int nameScore,
		IDictionary<int, BlendShapeDeltas> deltaCache,
		GeometryContext geometry,
		string leftKey,
		string rightKey)
	{
		TryGetDirectCoefficient(mesh, left, deltaCache, out var leftCoefficient, out _);
		TryGetDirectCoefficient(mesh, right, deltaCache, out var rightCoefficient, out _);
		var result = new RankedPair
		{
			Left = left,
			Right = right,
			Score = nameScore,
			LeftCoefficient = leftCoefficient,
			RightCoefficient = rightCoefficient,
		};

		if (combined != null
			&& TryFitPairToCombined(
				mesh,
				left,
				right,
				combined,
				deltaCache,
				out var fittedLeft,
				out var fittedRight,
				out var relativeError))
		{
			result.LeftCoefficient = fittedLeft;
			result.RightCoefficient = fittedRight;
			result.Score += Mathf.RoundToInt(50.0f * Mathf.Clamp01(1.0f - relativeError));
		}

		var leftQuality = EvaluateDeltas(mesh, GetDeltas(mesh, left.Index, deltaCache), result.LeftCoefficient);
		var rightQuality = EvaluateDeltas(mesh, GetDeltas(mesh, right.Index, deltaCache), result.RightCoefficient);
		var leftGeometry = EvaluateGeometry(geometry, GetDeltas(mesh, left.Index, deltaCache), leftKey);
		var rightGeometry = EvaluateGeometry(geometry, GetDeltas(mesh, right.Index, deltaCache), rightKey);
		result.Score += Mathf.RoundToInt(10.0f * (leftGeometry.Score + rightGeometry.Score - 1.8f));
		result.Usable = leftQuality.Usable
			&& rightQuality.Usable
			&& leftGeometry.Usable
			&& rightGeometry.Usable
			&& Mathf.Clamp01(0.72f + result.Score * 0.002f) * leftQuality.Score * leftGeometry.Score >= MinimumAutoConfidence
			&& Mathf.Clamp01(0.72f + result.Score * 0.002f) * rightQuality.Score * rightGeometry.Score >= MinimumAutoConfidence;

		return result;
	}

	private static bool TryFitPairToCombined(
		Mesh mesh,
		SourceShape left,
		SourceShape right,
		SourceShape combined,
		IDictionary<int, BlendShapeDeltas> deltaCache,
		out float leftCoefficient,
		out float rightCoefficient,
		out float relativeError)
	{
		leftCoefficient = 1.0f;
		rightCoefficient = 1.0f;
		relativeError = float.PositiveInfinity;
		if (!TryGetDirectCoefficient(mesh, combined, deltaCache, out var combinedCoefficient, out _))
		{
			return false;
		}

		var leftDeltas = GetDeltas(mesh, left.Index, deltaCache);
		var rightDeltas = GetDeltas(mesh, right.Index, deltaCache);
		var targetDeltas = ScaleDeltas(GetDeltas(mesh, combined.Index, deltaCache), combinedCoefficient);
		double targetEnergy = 0.0;
		for (var index = 0; index < mesh.vertexCount; index++)
		{
			var targetVertex = targetDeltas.Vertices[index];
			targetEnergy += Vector3.Dot(targetVertex, targetVertex);
		}

		if (targetEnergy <= 1e-12)
		{
			return false;
		}

		var weights = Enumerable.Repeat(1.0f, mesh.vertexCount).ToArray();
		for (var iteration = 0; iteration < 4; iteration++)
		{
			if (!TrySolveBoundedPair(leftDeltas, rightDeltas, targetDeltas, weights, out leftCoefficient, out rightCoefficient))
			{
				return false;
			}

			UpdateHuberWeights(mesh, leftDeltas, rightDeltas, targetDeltas, leftCoefficient, rightCoefficient, weights);
		}

		double error = 0.0;
		for (var index = 0; index < mesh.vertexCount; index++)
		{
			var reconstructed = leftDeltas.Vertices[index] * leftCoefficient
				+ rightDeltas.Vertices[index] * rightCoefficient;
			var difference = reconstructed - targetDeltas.Vertices[index];
			error += difference.sqrMagnitude;
		}

		relativeError = (float)Math.Sqrt(error / targetEnergy);
		if (!IsFinite(relativeError)
			|| relativeError > MaximumPairReconstructionError
			|| !EvaluateDeltas(mesh, leftDeltas, leftCoefficient).Usable
			|| !EvaluateDeltas(mesh, rightDeltas, rightCoefficient).Usable)
		{
			return false;
		}

		return true;
	}

	private static bool TrySolveBoundedPair(
		BlendShapeDeltas left,
		BlendShapeDeltas right,
		BlendShapeDeltas target,
		IReadOnlyList<float> weights,
		out float leftCoefficient,
		out float rightCoefficient)
	{
		leftCoefficient = 1.0f;
		rightCoefficient = 1.0f;
		double leftLeft = 0.0;
		double rightRight = 0.0;
		double leftRight = 0.0;
		double leftTarget = 0.0;
		double rightTarget = 0.0;
		for (var index = 0; index < left.Vertices.Length; index++)
		{
			var weight = weights[index];
			var leftVertex = left.Vertices[index];
			var rightVertex = right.Vertices[index];
			var targetVertex = target.Vertices[index];
			leftLeft += weight * Vector3.Dot(leftVertex, leftVertex);
			rightRight += weight * Vector3.Dot(rightVertex, rightVertex);
			leftRight += weight * Vector3.Dot(leftVertex, rightVertex);
			leftTarget += weight * Vector3.Dot(leftVertex, targetVertex);
			rightTarget += weight * Vector3.Dot(rightVertex, targetVertex);
		}

		if (leftLeft <= 1e-16 || rightRight <= 1e-16)
		{
			return false;
		}

		var candidates = new List<(float Left, float Right)>();
		var determinant = leftLeft * rightRight - leftRight * leftRight;
		if (determinant > Math.Max(leftLeft * rightRight, 1e-12) * 1e-8)
		{
			var unconstrainedLeft = (leftTarget * rightRight - rightTarget * leftRight) / determinant;
			var unconstrainedRight = (rightTarget * leftLeft - leftTarget * leftRight) / determinant;
			if (unconstrainedLeft >= MinimumFrameCoefficient
				&& unconstrainedLeft <= MaximumFrameCoefficient
				&& unconstrainedRight >= MinimumFrameCoefficient
				&& unconstrainedRight <= MaximumFrameCoefficient)
			{
				candidates.Add(((float)unconstrainedLeft, (float)unconstrainedRight));
			}
		}

		AddLeftBoundary(MinimumFrameCoefficient);
		AddLeftBoundary(MaximumFrameCoefficient);
		AddRightBoundary(MinimumFrameCoefficient);
		AddRightBoundary(MaximumFrameCoefficient);
		if (candidates.Count == 0)
		{
			return false;
		}

		var best = candidates
			.OrderBy(candidate => WeightedPairError(left, right, target, weights, candidate.Left, candidate.Right))
			.First();
		leftCoefficient = best.Left;
		rightCoefficient = best.Right;
		return IsFinite(leftCoefficient) && IsFinite(rightCoefficient);

		void AddLeftBoundary(float fixedLeft)
		{
			var solvedRight = (rightTarget - fixedLeft * leftRight) / rightRight;
			candidates.Add((fixedLeft, Mathf.Clamp((float)solvedRight, MinimumFrameCoefficient, MaximumFrameCoefficient)));
		}

		void AddRightBoundary(float fixedRight)
		{
			var solvedLeft = (leftTarget - fixedRight * leftRight) / leftLeft;
			candidates.Add((Mathf.Clamp((float)solvedLeft, MinimumFrameCoefficient, MaximumFrameCoefficient), fixedRight));
		}
	}

	private static double WeightedPairError(
		BlendShapeDeltas left,
		BlendShapeDeltas right,
		BlendShapeDeltas target,
		IReadOnlyList<float> weights,
		float leftCoefficient,
		float rightCoefficient)
	{
		double error = 0.0;
		for (var index = 0; index < left.Vertices.Length; index++)
		{
			var difference = left.Vertices[index] * leftCoefficient
				+ right.Vertices[index] * rightCoefficient
				- target.Vertices[index];
			error += weights[index] * difference.sqrMagnitude;
		}

		return error;
	}

	private static void UpdateHuberWeights(
		Mesh mesh,
		BlendShapeDeltas left,
		BlendShapeDeltas right,
		BlendShapeDeltas target,
		float leftCoefficient,
		float rightCoefficient,
		IList<float> weights)
	{
		var residuals = new float[left.Vertices.Length];
		var activeResiduals = new List<float>();
		var activityThreshold = Mathf.Max(mesh.bounds.size.magnitude, 0.0001f) * 0.000001f;
		for (var index = 0; index < left.Vertices.Length; index++)
		{
			var residual = (left.Vertices[index] * leftCoefficient
				+ right.Vertices[index] * rightCoefficient
				- target.Vertices[index]).magnitude;
			residuals[index] = residual;
			if (left.Vertices[index].magnitude > activityThreshold
				|| right.Vertices[index].magnitude > activityThreshold
				|| target.Vertices[index].magnitude > activityThreshold)
			{
				activeResiduals.Add(residual);
			}
		}

		if (activeResiduals.Count == 0)
		{
			return;
		}

		activeResiduals.Sort();
		var median = activeResiduals[activeResiduals.Count / 2];
		var robustScale = Mathf.Max(median / 0.6745f, activityThreshold);
		var huberThreshold = 1.345f * robustScale;
		for (var index = 0; index < residuals.Length; index++)
		{
			weights[index] = residuals[index] <= huberThreshold || residuals[index] <= 1e-12f
				? 1.0f
				: huberThreshold / residuals[index];
		}
	}

	private static double VertexEnergy(BlendShapeDeltas deltas)
	{
		double energy = 0.0;
		for (var index = 0; index < deltas.Vertices.Length; index++)
		{
			energy += deltas.Vertices[index].sqrMagnitude;
		}

		return energy;
	}

	private static BlendShapeDeltas GetDeltas(
		Mesh mesh,
		int blendShapeIndex,
		IDictionary<int, BlendShapeDeltas> deltaCache)
	{
		if (!deltaCache.TryGetValue(blendShapeIndex, out var deltas))
		{
			deltas = PerfectSyncMeshUtility.GetLastFrameDeltas(mesh, blendShapeIndex);
			deltaCache[blendShapeIndex] = deltas;
		}

		return deltas;
	}

	private static (BlendShapeDeltas Left, BlendShapeDeltas Right) SplitLeftRight(
		SkinnedMeshRenderer renderer,
		Mesh mesh,
		BlendShapeDeltas source)
	{
		var vertices = mesh.vertices;
		var midpoint = mesh.bounds.center;
		var leftAxis = Vector3.left;
		var blendWidth = Mathf.Max(mesh.bounds.size.x * 0.04f, 0.0001f);
		var animator = renderer.GetComponentInParent<Animator>();
		if (animator != null && animator.isHuman)
		{
			var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
			var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
			if (leftEye != null && rightEye != null)
			{
				var leftPosition = renderer.transform.InverseTransformPoint(leftEye.position);
				var rightPosition = renderer.transform.InverseTransformPoint(rightEye.position);
				midpoint = (leftPosition + rightPosition) * 0.5f;
				leftAxis = (leftPosition - rightPosition).normalized;
				blendWidth = Mathf.Max(Vector3.Distance(leftPosition, rightPosition) * 0.12f, 0.0001f);
			}
		}

		var leftVertices = new Vector3[mesh.vertexCount];
		var leftNormals = new Vector3[mesh.vertexCount];
		var leftTangents = new Vector3[mesh.vertexCount];
		var rightVertices = new Vector3[mesh.vertexCount];
		var rightNormals = new Vector3[mesh.vertexCount];
		var rightTangents = new Vector3[mesh.vertexCount];
		for (var i = 0; i < mesh.vertexCount; i++)
		{
			var signedDistance = Vector3.Dot(vertices[i] - midpoint, leftAxis);
			var leftWeight = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(-blendWidth, blendWidth, signedDistance));
			var rightWeight = 1.0f - leftWeight;
			leftVertices[i] = source.Vertices[i] * leftWeight;
			leftNormals[i] = source.Normals[i] * leftWeight;
			leftTangents[i] = source.Tangents[i] * leftWeight;
			rightVertices[i] = source.Vertices[i] * rightWeight;
			rightNormals[i] = source.Normals[i] * rightWeight;
			rightTangents[i] = source.Tangents[i] * rightWeight;
		}

		return (
			new BlendShapeDeltas(leftVertices, leftNormals, leftTangents),
			new BlendShapeDeltas(rightVertices, rightNormals, rightTangents));
	}

	private static List<SourceShape> CreateSources(Mesh mesh, ISet<string> canonicalNames)
	{
		var sources = new List<SourceShape>();
		var section = "";
		for (var index = 0; index < mesh.blendShapeCount; index++)
		{
			var name = mesh.GetBlendShapeName(index);
			if (TryReadSection(name, out var nextSection))
			{
				section = nextSection;
				continue;
			}

			if (canonicalNames.Contains(name))
			{
				continue;
			}

			var source = CreateSource(name, index);
			source.Section = section;
			sources.Add(source);
		}

		return sources;
	}

	private static bool TryReadSection(string name, out string section)
	{
		section = "";
		var trimmed = name.Trim();
		var isEqualsHeader = trimmed.StartsWith("==", StringComparison.Ordinal);
		var isUnderscoreHeader = trimmed.StartsWith("____", StringComparison.Ordinal)
			&& trimmed.EndsWith("____", StringComparison.Ordinal);
		if (!isEqualsHeader && !isUnderscoreHeader)
		{
			return false;
		}

		var compact = Compact(name);
		if (compact.Contains("eyebrow") || compact.Contains("brow") || compact.Contains("眉"))
		{
			section = "brow";
		}
		else if (compact.Contains("eyelid") || compact.Contains("eye") || compact.Contains("瞼"))
		{
			section = "eye";
		}
		else if (compact.Contains("mouth") || compact.Contains("口"))
		{
			section = "mouth";
		}
		else if (compact.Contains("tongue") || compact.Contains("tang") || compact.Contains("舌"))
		{
			section = "tongue";
		}

		return true;
	}

	private static SourceShape CreateSource(string name, int index)
	{
		var compact = Compact(name);
		var side = Side.None;
		var baseName = compact;
		if (TryStripSide(name, compact, "left", "l", "左", out var leftBase))
		{
			side = Side.Left;
			baseName = leftBase;
		}
		else if (TryStripSide(name, compact, "right", "r", "右", out var rightBase))
		{
			side = Side.Right;
			baseName = rightBase;
		}

		return new SourceShape
		{
			Name = name,
			CompactName = compact,
			BaseName = baseName,
			Side = side,
			Index = index,
		};
	}

	private static bool TryStripSide(string original, string compact, string word, string letter, string japanese, out string baseName)
	{
		if (compact.EndsWith(word, StringComparison.Ordinal))
		{
			baseName = compact.Substring(0, compact.Length - word.Length);
			return true;
		}

		if (compact.EndsWith(japanese, StringComparison.Ordinal))
		{
			baseName = compact.Substring(0, compact.Length - japanese.Length);
			return true;
		}

		var trimmed = original.TrimEnd();
		if (trimmed.Length >= 2 && char.ToLowerInvariant(trimmed[trimmed.Length - 1]) == letter[0])
		{
			var separator = trimmed[trimmed.Length - 2];
			if (separator is '_' or '.' or '-' or ' ')
			{
				baseName = Compact(trimmed.Substring(0, trimmed.Length - 2));
				return true;
			}
		}

		baseName = compact;
		return false;
	}

	private static int AliasScore(string compactName, IReadOnlyList<string> aliases)
	{
		var best = 0;
		foreach (var rawAlias in aliases)
		{
			var alias = Compact(rawAlias);
			if (compactName == alias)
			{
				best = Math.Max(best, 100);
			}
			else if (compactName.EndsWith(alias, StringComparison.Ordinal))
			{
				best = Math.Max(best, 90);
			}
			else if (compactName.StartsWith(alias, StringComparison.Ordinal))
			{
				best = Math.Max(best, 85);
			}
			else if (compactName.Contains(alias))
			{
				best = Math.Max(best, 70);
			}
		}

		return best;
	}

	private static string Compact(string value)
	{
		return new string(value
			.ToLowerInvariant()
			.Where(character => char.IsLetterOrDigit(character) || character > 127)
			.ToArray());
	}

	private static PairRule Pair(string leftKey, string rightKey, params string[] aliases)
	{
		return new PairRule { LeftKey = leftKey, RightKey = rightKey, Aliases = aliases };
	}

	private static PairRule PairInSection(
		string leftKey,
		string rightKey,
		string section,
		string[] sectionAliases,
		params string[] aliases)
	{
		return new PairRule
		{
			LeftKey = leftKey,
			RightKey = rightKey,
			Aliases = aliases,
			Section = section,
			SectionAliases = sectionAliases,
		};
	}

	private static SingleRule Single(string key, params string[] aliases)
	{
		return new SingleRule { Key = key, Aliases = aliases };
	}

	private static SingleRule SinglePair(string key, params string[] aliases)
	{
		var rule = Single(key, aliases);
		rule.MergePair = true;
		return rule;
	}

	private static SingleRule SingleInSection(
		string key,
		string section,
		string[] sectionAliases,
		params string[] aliases)
	{
		return new SingleRule
		{
			Key = key,
			Aliases = aliases,
			Section = section,
			SectionAliases = sectionAliases,
		};
	}

	private static SingleRule SinglePairInSection(
		string key,
		string section,
		string[] sectionAliases,
		params string[] aliases)
	{
		var rule = SingleInSection(key, section, sectionAliases, aliases);
		rule.MergePair = true;
		return rule;
	}
}
}
