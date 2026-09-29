using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

internal sealed class PerfectSyncSession
{
	private readonly string[] defaultVocabulary;
	private readonly Dictionary<string, PerfectSyncPreviewState> previewStates = new(StringComparer.Ordinal);
	private readonly Dictionary<string, BlendShapeDeltas> sourceDeltaCache = new();

	public PerfectSyncSession(string[] defaultVocabulary)
	{
		this.defaultVocabulary = defaultVocabulary;
		ResetVocabulary();
		EnsureKeyStates();
	}

	public Dictionary<string, PerfectSyncKeyState> KeyStates { get; } = new();
	public List<string> Vocabulary { get; } = new();
	public SkinnedMeshRenderer TargetRenderer { get; private set; }
	public PerfectSyncMappingProfile Profile { get; private set; }
	public string SelectedKey { get; private set; } = "";

	public void OnDisable()
	{
		RestorePreview();
		FlushProfile();
	}

	public void ChangeTarget(SkinnedMeshRenderer nextRenderer)
	{
		RestorePreview();
		FlushProfile();
		TargetRenderer = nextRenderer;
		Profile = null;
		SelectedKey = "";
		sourceDeltaCache.Clear();
		ResetKeyStates();
		ResetVocabulary();
		var globalVocabulary = Vocabulary.ToArray();

		if (TargetRenderer != null && TargetRenderer.sharedMesh != null)
		{
			Profile = PerfectSyncProfileStore.LoadOrCreateProfile(TargetRenderer, globalVocabulary);
			PerfectSyncProfileStore.LoadInto(Profile, KeyStates, Vocabulary, globalVocabulary);
			if (Profile.workingMesh != null && TargetRenderer.sharedMesh != Profile.workingMesh)
			{
				Undo.RecordObject(TargetRenderer, "Assign PerfectSync Working Mesh");
				TargetRenderer.sharedMesh = Profile.workingMesh;
				EditorUtility.SetDirty(TargetRenderer);
			}
		}

		Scan();
	}

	public void Scan()
	{
		RestorePreview();
		sourceDeltaCache.Clear();
		EnsureKeyStates();
		foreach (var key in PerfectSyncKeys.Names)
		{
			UpdateStateForKey(key);
		}
	}

	public void SelectKey(string keyName)
	{
		RestorePreview();
		SelectedKey = keyName;
		if (TargetRenderer?.sharedMesh == null)
		{
			return;
		}

		UpdateStateForKey(keyName);
		ApplyDefaultPreview(KeyStates[keyName]);
	}

	public void CreateEmptyBlendShape(string keyName)
	{
		RestorePreview();
		SelectedKey = keyName;
		var mesh = TargetRenderer?.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		var index = PerfectSyncMeshUtility.FindBlendShapeIndexIgnoreCase(mesh, keyName, out _, out _);
		if (index >= 0)
		{
			UpdateStateForKey(keyName);
			return;
		}

		mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		PerfectSyncMeshUtility.AddEmptyBlendShape(mesh, keyName);
		AssetDatabase.SaveAssets();
		sourceDeltaCache.Remove(keyName);
		UpdateStateForKey(keyName);
		FlushProfile();
	}

	public void FixBlendShapeCase(PerfectSyncKeyState state)
	{
		RestorePreview();
		var mesh = TargetRenderer?.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		if (string.IsNullOrWhiteSpace(state.meshBlendShapeName) || state.meshBlendShapeName == state.keyName)
		{
			UpdateStateForKey(state.keyName);
			return;
		}

		mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		PerfectSyncMeshUtility.RenameBlendShape(mesh, state.meshBlendShapeName, state.keyName);
		AssetDatabase.SaveAssets();
		sourceDeltaCache.Remove(state.meshBlendShapeName);
		sourceDeltaCache.Remove(state.keyName);
		UpdateStateForKey(state.keyName);
		FlushProfile();
	}

	public int FixAllCaseMismatches()
	{
		RestorePreview();
		var mesh = TargetRenderer?.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		var renameMap = KeyStates.Values
			.Where(IsCaseMismatch)
			.ToDictionary(state => state.meshBlendShapeName, state => state.keyName);
		if (renameMap.Count == 0)
		{
			return 0;
		}

		mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		PerfectSyncMeshUtility.RenameBlendShapes(mesh, renameMap);
		AssetDatabase.SaveAssets();
		sourceDeltaCache.Clear();
		Scan();
		FlushProfile();
		return renameMap.Count;
	}

	public int CreateEmptyBlendShapesForUndefined()
	{
		RestorePreview();
		var mesh = TargetRenderer?.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		var names = KeyStates.Values
			.Where(state => state.status == PerfectSyncKeyStatus.Undefined)
			.Select(state => state.keyName)
			.ToList();
		if (names.Count == 0)
		{
			return 0;
		}

		mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		PerfectSyncMeshUtility.AddEmptyBlendShapes(mesh, names);
		AssetDatabase.SaveAssets();
		foreach (var name in names)
		{
			sourceDeltaCache.Remove(name);
		}

		Scan();
		FlushProfile();
		return names.Count;
	}

	public int ResetAllPerfectSyncKeys()
	{
		RestorePreview();
		var mesh = TargetRenderer?.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		var removedCount = RemoveKeysPreservingWeights(mesh, PerfectSyncKeys.Names);
		foreach (var state in KeyStates.Values)
		{
			state.sources.Clear();
			state.generatedSource = "";
			state.generatedMethod = "";
			state.warning = "";
		}

		SelectedKey = "";
		sourceDeltaCache.Clear();
		AssetDatabase.SaveAssets();
		Scan();
		FlushProfile();
		return removedCount;
	}

	public void ResetKey(string keyName)
	{
		if (!KeyStates.TryGetValue(keyName, out var state))
		{
			throw new ArgumentException("Unknown Perfect Sync Key", nameof(keyName));
		}

		RestorePreview();
		var mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		RemoveKeysPreservingWeights(mesh, new[] { keyName });
		state.sources.Clear();
		state.generatedSource = "";
		state.generatedMethod = "";
		AssetDatabase.SaveAssets();
		Scan();
		FlushProfile();
	}

	private int RemoveKeysPreservingWeights(Mesh mesh, IReadOnlyCollection<string> names)
	{
		var weights = new Dictionary<string, float>(StringComparer.Ordinal);
		for (var i = 0; i < mesh.blendShapeCount; i++)
		{
			weights[mesh.GetBlendShapeName(i)] = TargetRenderer.GetBlendShapeWeight(i);
		}

		Undo.RecordObject(TargetRenderer, "Reset PerfectSync Key Weights");
		// Clear old slots as well: Unity may retain trailing weights after removing shapes.
		for (var i = 0; i < mesh.blendShapeCount; i++)
		{
			TargetRenderer.SetBlendShapeWeight(i, 0.0f);
		}

		var count = PerfectSyncMeshUtility.RemoveBlendShapesIgnoreCase(mesh, names);
		for (var i = 0; i < mesh.blendShapeCount; i++)
		{
			TargetRenderer.SetBlendShapeWeight(i, weights[mesh.GetBlendShapeName(i)]);
		}

		EditorUtility.SetDirty(TargetRenderer);
		return count;
	}

	public void ClearSelection()
	{
		RestorePreview();
		SelectedKey = "";
	}

	public void UpdateAssignment(PerfectSyncKeyState state, string sourceName, bool enabled, float coefficient)
	{
		RestorePreview();
		var assignment = state.sources.FirstOrDefault(source => source.blendShapeName == sourceName);
		if (assignment == null)
		{
			assignment = new PerfectSyncSourceAssignment
			{
				blendShapeName = sourceName,
			};
			state.sources.Add(assignment);
		}

		assignment.enabled = enabled;
		assignment.coefficient = coefficient;
		ApplyAssignment(state);
		ApplyDefaultPreview(state);
	}

	public PerfectSyncAutoSetupPlan AnalyzeAutoSetup(bool overwriteConfigured = false)
	{
		return PerfectSyncAutoSetup.Analyze(TargetRenderer, KeyStates, overwriteConfigured);
	}

	public int ApplyAutoSetup(PerfectSyncAutoSetupPlan plan, bool overwriteConfigured = false)
	{
		RestorePreview();
		if (plan == null || plan.Suggestions.Count == 0)
		{
			return 0;
		}

		var mesh = TargetRenderer?.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		var replacements = new Dictionary<string, BlendShapeDeltas>(StringComparer.Ordinal);
		var pendingAssignments = new Dictionary<string, PerfectSyncSourceAssignment>(StringComparer.Ordinal);
		var generated = new Dictionary<string, PerfectSyncAutoSetupSuggestion>(StringComparer.Ordinal);
		foreach (var suggestion in plan.Suggestions)
		{
			if (!KeyStates.TryGetValue(suggestion.KeyName, out var state)
				|| IsCaseMismatch(state)
				|| (!overwriteConfigured && IsConfigured(state)))
			{
				continue;
			}

			if (suggestion.Kind == PerfectSyncAutoSetupKind.Direct)
			{
				if (mesh.GetBlendShapeIndex(suggestion.SourceName) < 0)
				{
					continue;
				}

				var assignment = new PerfectSyncSourceAssignment
				{
					blendShapeName = suggestion.SourceName,
					coefficient = suggestion.Coefficient,
					enabled = true,
				};
				pendingAssignments[state.keyName] = assignment;
				replacements[state.keyName] = PerfectSyncMeshUtility.ComposeDeltas(
					mesh,
					new[] { assignment },
					sourceDeltaCache);
			}
			else if (suggestion.Deltas.HasValue)
			{
				replacements[state.keyName] = suggestion.Deltas.Value;
				generated[state.keyName] = suggestion;
			}
			else
			{
				continue;
			}

		}

		PerfectSyncMeshUtility.AddOrReplaceBlendShapes(mesh, replacements);
		foreach (var keyName in replacements.Keys)
		{
			var state = KeyStates[keyName];
			state.sources.Clear();
			state.generatedSource = generated.TryGetValue(keyName, out var suggestion) ? suggestion.SourceName : "";
			state.generatedMethod = suggestion?.Method ?? "";
			if (pendingAssignments.TryGetValue(keyName, out var assignment))
			{
				state.sources.Add(assignment);
			}
		}
		AssetDatabase.SaveAssets();
		sourceDeltaCache.Clear();
		Scan();
		FlushProfile();
		return replacements.Count;
	}

	private static bool IsConfigured(PerfectSyncKeyState state)
	{
		return state.sources.Any(source => source.enabled)
			|| state.status is not (PerfectSyncKeyStatus.Undefined or PerfectSyncKeyStatus.Empty);
	}

	public void RestoreBackup(PerfectSyncBlendShapeBackup backup)
	{
		RestorePreview();
		var mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		PerfectSyncMeshUtility.RestoreBackup(mesh, backup);
		AssetDatabase.SaveAssets();
		sourceDeltaCache.Clear();
		Scan();
		FlushProfile();
	}

	public float PreviewWeight(string sourceName, int sourceIndex)
	{
		return previewStates.TryGetValue(sourceName, out var previewState)
			? previewState.previewWeight
			: TargetRenderer.GetBlendShapeWeight(sourceIndex);
	}

	public void SetPreview(string sourceName, int sourceIndex, float weight)
	{
		weight = Mathf.Clamp(weight, 0.0f, 100.0f);
		if (previewStates.TryGetValue(sourceName, out var previewState))
		{
			SetPreviewWeight(previewState, sourceIndex, weight);
			return;
		}

		RestorePreview();
		AddPreview(sourceName, sourceIndex, weight);
	}

	public void SaveVocabulary()
	{
		PerfectSyncProfileStore.SaveGlobalVocabulary(Vocabulary);
		FlushProfile();
	}

	public void FlushProfile()
	{
		if (Profile == null)
		{
			return;
		}

		PerfectSyncProfileStore.Flush(Profile, TargetRenderer, KeyStates, Vocabulary);
	}

	private void ApplyAssignment(PerfectSyncKeyState state)
	{
		var mesh = PerfectSyncMeshUtility.EnsureEditableMesh(TargetRenderer, Profile);
		var deltas = PerfectSyncMeshUtility.ComposeDeltas(mesh, state.sources, sourceDeltaCache);
		PerfectSyncMeshUtility.AddOrReplaceBlendShape(mesh, state.keyName, deltas);
		state.generatedSource = "";
		state.generatedMethod = "";
		AssetDatabase.SaveAssets();
		sourceDeltaCache.Remove(state.keyName);
		UpdateStateForKey(state.keyName);
		FlushProfile();
	}

	private void RestorePreview()
	{
		if (previewStates.Count == 0)
		{
			return;
		}

		var snapshots = previewStates.Values.ToList();
		previewStates.Clear();
		if (TargetRenderer == null || TargetRenderer.sharedMesh == null)
		{
			return;
		}

		foreach (var previewState in snapshots)
		{
			var index = TargetRenderer.sharedMesh.GetBlendShapeIndex(previewState.blendShapeName);
			if (index < 0)
			{
				continue;
			}

			TargetRenderer.SetBlendShapeWeight(index, previewState.originalWeight);
			if (!previewState.hadPrefabOverride)
			{
				RemoveBlendShapeWeightPrefabOverride(index);
			}
		}
	}

	private void ApplyDefaultPreview(PerfectSyncKeyState state)
	{
		if (TargetRenderer?.sharedMesh == null || state == null)
		{
			return;
		}

		var assignedSources = state.sources.Where(source => source.enabled).ToList();
		if (assignedSources.Count > 0)
		{
			foreach (var source in assignedSources)
			{
				var sourceIndex = TargetRenderer.sharedMesh.GetBlendShapeIndex(source.blendShapeName);
				if (sourceIndex >= 0)
				{
					AddPreview(source.blendShapeName, sourceIndex, 100.0f);
				}
			}

			return;
		}

		if (state.status is PerfectSyncKeyStatus.Existing or PerfectSyncKeyStatus.Assigned)
		{
			var keyIndex = TargetRenderer.sharedMesh.GetBlendShapeIndex(state.keyName);
			if (keyIndex >= 0)
			{
				AddPreview(state.keyName, keyIndex, 100.0f);
			}
		}
	}

	private void AddPreview(string blendShapeName, int blendShapeIndex, float weight)
	{
		if (previewStates.ContainsKey(blendShapeName))
		{
			return;
		}

		var previewState = new PerfectSyncPreviewState
		{
			blendShapeName = blendShapeName,
			originalWeight = TargetRenderer.GetBlendShapeWeight(blendShapeIndex),
			hadPrefabOverride = HasBlendShapeWeightPrefabOverride(blendShapeIndex),
		};
		previewStates.Add(blendShapeName, previewState);
		SetPreviewWeight(previewState, blendShapeIndex, weight);
	}

	private void SetPreviewWeight(PerfectSyncPreviewState previewState, int blendShapeIndex, float weight)
	{
		if (Mathf.Abs(previewState.previewWeight - weight) < 0.001f
			&& Mathf.Abs(TargetRenderer.GetBlendShapeWeight(blendShapeIndex) - weight) < 0.001f)
		{
			return;
		}

		previewState.previewWeight = weight;
		TargetRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
	}

	private bool HasBlendShapeWeightPrefabOverride(int blendShapeIndex)
	{
		var property = BlendShapeWeightProperty(blendShapeIndex);
		return property != null && property.prefabOverride;
	}

	private void RemoveBlendShapeWeightPrefabOverride(int blendShapeIndex)
	{
		if (TargetRenderer == null || !PrefabUtility.IsPartOfPrefabInstance(TargetRenderer))
		{
			return;
		}

		var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(TargetRenderer);
		var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(TargetRenderer);
		if (instanceRoot == null || sourceRenderer == null)
		{
			return;
		}

		var propertyPath = $"m_BlendShapeWeights.Array.data[{blendShapeIndex}]";
		var modifications = PrefabUtility.GetPropertyModifications(instanceRoot);
		if (modifications == null || modifications.Length == 0)
		{
			return;
		}

		var filtered = modifications
			.Where(modification => modification == null
				|| modification.target != sourceRenderer
				|| modification.propertyPath != propertyPath)
			.ToArray();
		if (filtered.Length != modifications.Length)
		{
			PrefabUtility.SetPropertyModifications(instanceRoot, filtered);
		}
	}

	private SerializedProperty BlendShapeWeightProperty(int blendShapeIndex)
	{
		if (TargetRenderer == null || !PrefabUtility.IsPartOfPrefabInstance(TargetRenderer))
		{
			return null;
		}

		var serializedRenderer = new SerializedObject(TargetRenderer);
		serializedRenderer.UpdateIfRequiredOrScript();
		var weights = serializedRenderer.FindProperty("m_BlendShapeWeights");
		if (weights == null || blendShapeIndex < 0 || blendShapeIndex >= weights.arraySize)
		{
			return null;
		}

		return weights.GetArrayElementAtIndex(blendShapeIndex);
	}

	private void ResetVocabulary()
	{
		Vocabulary.Clear();
		Vocabulary.AddRange(PerfectSyncProfileStore.LoadGlobalVocabulary(defaultVocabulary));
	}

	private void ResetKeyStates()
	{
		KeyStates.Clear();
		EnsureKeyStates();
	}

	private void EnsureKeyStates()
	{
		foreach (var key in PerfectSyncKeys.Names)
		{
			if (!KeyStates.ContainsKey(key))
			{
				KeyStates[key] = new PerfectSyncKeyState
				{
					keyName = key,
				};
			}
		}
	}

	private void UpdateStateForKey(string keyName)
	{
		EnsureKeyStates();
		var state = KeyStates[keyName];
		state.warning = "";
		state.meshBlendShapeName = "";
		var mesh = TargetRenderer?.sharedMesh;
		var hasAssignment = state.sources.Any(source => source.enabled);
		if (mesh == null)
		{
			state.status = hasAssignment ? PerfectSyncKeyStatus.Warning : PerfectSyncKeyStatus.Undefined;
			return;
		}

		foreach (var source in state.sources.Where(source => source.enabled))
		{
			if (mesh.GetBlendShapeIndex(source.blendShapeName) < 0)
			{
				state.status = PerfectSyncKeyStatus.Warning;
				state.warning = $"割り当て元の BlendShape が見つかりません: {source.blendShapeName}";
				return;
			}
		}

		var index = PerfectSyncMeshUtility.FindBlendShapeIndexIgnoreCase(mesh, keyName, out var actualName, out var exactCase);
		if (index < 0)
		{
			state.status = PerfectSyncKeyStatus.Undefined;
			return;
		}

		state.meshBlendShapeName = actualName;
		if (!exactCase)
		{
			state.status = PerfectSyncKeyStatus.Warning;
			state.warning = $"BlendShape 名の大文字・小文字が正式名と異なります: {actualName} → {keyName}";
			return;
		}

		state.status = PerfectSyncMeshUtility.IsBlendShapeEmpty(mesh, index)
			? PerfectSyncKeyStatus.Empty
			: hasAssignment || !string.IsNullOrEmpty(state.generatedSource)
				? PerfectSyncKeyStatus.Assigned
				: PerfectSyncKeyStatus.Existing;
	}

	private static bool IsCaseMismatch(PerfectSyncKeyState state)
	{
		return state.status == PerfectSyncKeyStatus.Warning && !string.IsNullOrEmpty(state.meshBlendShapeName);
	}
}
}
