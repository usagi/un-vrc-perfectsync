using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

internal static class PerfectSyncProfileStore
{
	public const string ProfileDirectory = "Assets/U.N. VRC PerfectSync";
	private const string GlobalVocabularyKey = "UNVRC.PerfectSync.GlobalVocabulary";

	public static List<string> LoadGlobalVocabulary(IEnumerable<string> defaults)
	{
		var raw = EditorPrefs.GetString(GlobalVocabularyKey, "");
		if (string.IsNullOrWhiteSpace(raw))
		{
			return DistinctVocabulary(defaults);
		}

		var values = raw
			.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(value => value.Trim())
			.Where(value => value.Length > 0);
		var vocabulary = DistinctVocabulary(values);
		return vocabulary.Count > 0 ? vocabulary : DistinctVocabulary(defaults);
	}

	public static void SaveGlobalVocabulary(IEnumerable<string> vocabulary)
	{
		EditorPrefs.SetString(GlobalVocabularyKey, string.Join("\n", DistinctVocabulary(vocabulary)));
	}

	public static PerfectSyncMappingProfile LoadOrCreateProfile(SkinnedMeshRenderer renderer, IEnumerable<string> defaultVocabulary)
	{
		var profile = FindProfile(renderer);
		if (profile != null)
		{
			return profile;
		}

		EnsureProfileDirectory();
		var meshName = renderer.sharedMesh != null ? renderer.sharedMesh.name : renderer.name;
		var fileName = $"{SanitizeFileName(renderer.gameObject.name)}_{SanitizeFileName(meshName)}.PerfectSyncProfile.asset";
		var path = AssetDatabase.GenerateUniqueAssetPath($"{ProfileDirectory}/{fileName}");
		profile = ScriptableObject.CreateInstance<PerfectSyncMappingProfile>();
		profile.createdAt = DateTime.UtcNow.ToString("O");
		profile.vocabulary = DistinctVocabulary(defaultVocabulary).ToArray();
		UpdateProfileTarget(profile, renderer);
		AssetDatabase.CreateAsset(profile, path);
		AssetDatabase.SaveAssets();
		return profile;
	}

	public static void UpdateProfileTarget(PerfectSyncMappingProfile profile, SkinnedMeshRenderer renderer)
	{
		profile.targetRendererPath = HierarchyPath(renderer.transform);
		if (profile.targetMesh == null)
		{
			profile.targetMesh = renderer.sharedMesh;
			profile.targetMeshGuid = MeshGuid(renderer.sharedMesh);
		}

		profile.updatedAt = DateTime.UtcNow.ToString("O");
		EditorUtility.SetDirty(profile);
	}

	public static void SetWorkingMesh(PerfectSyncMappingProfile profile, Mesh workingMesh)
	{
		profile.workingMesh = workingMesh;
		profile.workingMeshGuid = MeshGuid(workingMesh);
		profile.updatedAt = DateTime.UtcNow.ToString("O");
		EditorUtility.SetDirty(profile);
	}

	public static string CreateWorkingMeshPath(SkinnedMeshRenderer renderer, Mesh sourceMesh)
	{
		EnsureProfileDirectory();
		var rendererName = renderer != null ? renderer.gameObject.name : "Face";
		var meshName = sourceMesh != null ? sourceMesh.name : "Mesh";
		var fileName = $"{SanitizeFileName(rendererName)}_{SanitizeFileName(meshName)}.PerfectSyncMesh.asset";
		return AssetDatabase.GenerateUniqueAssetPath($"{ProfileDirectory}/{fileName}");
	}

	public static void Flush(
		PerfectSyncMappingProfile profile,
		SkinnedMeshRenderer renderer,
		IReadOnlyDictionary<string, PerfectSyncKeyState> keyStates,
		IEnumerable<string> vocabulary)
	{
		if (profile == null)
		{
			return;
		}

		if (renderer != null)
		{
			UpdateProfileTarget(profile, renderer);
		}

		profile.keys = keyStates.Values
			.Where(state => state.sources.Count > 0 || !string.IsNullOrEmpty(state.generatedSource) || !string.IsNullOrWhiteSpace(state.warning))
			.Select(ToProfile)
			.ToArray();
		profile.vocabulary = DistinctVocabulary(vocabulary).ToArray();
		profile.updatedAt = DateTime.UtcNow.ToString("O");
		EditorUtility.SetDirty(profile);
		AssetDatabase.SaveAssets();
	}

	public static void LoadInto(
		PerfectSyncMappingProfile profile,
		IDictionary<string, PerfectSyncKeyState> keyStates,
		ICollection<string> vocabulary,
		IEnumerable<string> fallbackVocabulary)
	{
		foreach (var state in keyStates.Values)
		{
			state.sources.Clear();
			state.generatedSource = "";
			state.generatedMethod = "";
			state.warning = "";
			state.status = PerfectSyncKeyStatus.Undefined;
		}

		if (profile != null)
		{
			foreach (var keyProfile in profile.keys ?? Array.Empty<PerfectSyncKeyProfile>())
			{
				if (!keyStates.TryGetValue(keyProfile.keyName, out var state))
				{
					continue;
				}

				state.sources.Clear();
				state.generatedSource = keyProfile.generatedSource ?? "";
				state.generatedMethod = keyProfile.generatedMethod ?? "";
				foreach (var source in keyProfile.sources ?? Array.Empty<PerfectSyncSourceProfile>())
				{
					state.sources.Add(new PerfectSyncSourceAssignment
					{
						blendShapeName = source.blendShapeName,
						coefficient = source.coefficient,
						enabled = source.enabled,
					});
				}

				state.warning = keyProfile.notes ?? "";
			}
		}

		vocabulary.Clear();
		var sourceVocabulary = profile != null && profile.vocabulary != null && profile.vocabulary.Length > 0 ? profile.vocabulary : fallbackVocabulary;
		foreach (var value in DistinctVocabulary(sourceVocabulary))
		{
			vocabulary.Add(value);
		}
	}

	private static PerfectSyncMappingProfile FindProfile(SkinnedMeshRenderer renderer)
	{
		if (renderer == null || renderer.sharedMesh == null)
		{
			return null;
		}

		var profiles = AssetDatabase.FindAssets("t:PerfectSyncMappingProfile")
			.Select(guid => AssetDatabase.GUIDToAssetPath(guid))
			.Select(AssetDatabase.LoadAssetAtPath<PerfectSyncMappingProfile>)
			.Where(profile => profile != null)
			.ToList();
		// A working mesh is an explicit link. A shared original mesh alone is not.
		var workingProfile = profiles.FirstOrDefault(profile => profile.workingMesh == renderer.sharedMesh);
		if (workingProfile != null)
		{
			return workingProfile;
		}

		var rendererPath = HierarchyPath(renderer.transform);
		foreach (var profile in profiles)
		{
			if (profile.targetMesh == renderer.sharedMesh
				&& !string.IsNullOrEmpty(profile.targetRendererPath) && profile.targetRendererPath == rendererPath)
			{
				return profile;
			}
		}

		return null;
	}

	private static PerfectSyncKeyProfile ToProfile(PerfectSyncKeyState state)
	{
		return new PerfectSyncKeyProfile
		{
			keyName = state.keyName,
			generatedSource = state.generatedSource,
			generatedMethod = state.generatedMethod,
			notes = state.warning,
			sources = state.sources
				.Select(source => new PerfectSyncSourceProfile
				{
					blendShapeName = source.blendShapeName,
					coefficient = source.coefficient,
					enabled = source.enabled,
				})
				.ToArray(),
		};
	}

	private static List<string> DistinctVocabulary(IEnumerable<string> vocabulary)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();
		foreach (var value in vocabulary)
		{
			var trimmed = value.Trim();
			if (trimmed.Length == 0 || !seen.Add(trimmed))
			{
				continue;
			}

			result.Add(trimmed);
		}

		return result;
	}

	private static void EnsureProfileDirectory()
	{
		if (AssetDatabase.IsValidFolder(ProfileDirectory))
		{
			return;
		}

		if (!AssetDatabase.IsValidFolder("Assets"))
		{
			return;
		}

		AssetDatabase.CreateFolder("Assets", "U.N. VRC PerfectSync");
	}

	private static string MeshGuid(Mesh mesh)
	{
		if (mesh == null)
		{
			return "";
		}

		var path = AssetDatabase.GetAssetPath(mesh);
		return string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
	}

	private static string HierarchyPath(Transform transform)
	{
		var parts = new Stack<string>();
		var current = transform;
		while (current != null)
		{
			parts.Push(current.name);
			current = current.parent;
		}

		return string.Join("/", parts);
	}

	private static string SanitizeFileName(string value)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
		var sanitized = new string(chars).Trim();
		return string.IsNullOrEmpty(sanitized) ? "PerfectSync" : sanitized;
	}
}
}
