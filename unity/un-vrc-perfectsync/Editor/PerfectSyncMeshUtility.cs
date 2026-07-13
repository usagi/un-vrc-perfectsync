using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

internal static class PerfectSyncMeshUtility
{
	private const float EmptyEpsilonScale = 0.000001f;

	public static Mesh EnsureEditableMesh(SkinnedMeshRenderer renderer, PerfectSyncMappingProfile profile)
	{
		var mesh = renderer.sharedMesh;
		if (mesh == null)
		{
			throw new InvalidOperationException("Face Object に Mesh が設定されていません。");
		}

		if (profile != null && profile.workingMesh != null)
		{
			if (renderer.sharedMesh != profile.workingMesh)
			{
				Undo.RecordObject(renderer, "Assign PerfectSync Working Mesh");
				renderer.sharedMesh = profile.workingMesh;
				EditorUtility.SetDirty(renderer);
			}

			return profile.workingMesh;
		}

		var copyPath = PerfectSyncProfileStore.CreateWorkingMeshPath(renderer, mesh);

		var copy = UnityEngine.Object.Instantiate(mesh);
		copy.name = $"{mesh.name}_PerfectSync";
		AssetDatabase.CreateAsset(copy, copyPath);
		AssetDatabase.SaveAssets();
		Undo.RecordObject(renderer, "Assign PerfectSync Working Mesh");
		renderer.sharedMesh = copy;
		EditorUtility.SetDirty(renderer);
		if (profile != null)
		{
			if (profile.targetMesh == null)
			{
				profile.targetMesh = mesh;
				profile.targetMeshGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh));
			}

			PerfectSyncProfileStore.SetWorkingMesh(profile, copy);
		}

		return copy;
	}

	public static bool IsBlendShapeEmpty(Mesh mesh, int blendShapeIndex)
	{
		if (mesh == null || blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount)
		{
			return true;
		}

		var vertexCount = mesh.vertexCount;
		var vertices = new Vector3[vertexCount];
		var normals = new Vector3[vertexCount];
		var tangents = new Vector3[vertexCount];
		var maxMagnitude = 0.0f;
		var sumMagnitude = 0.0f;
		var frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
		for (var frame = 0; frame < frameCount; frame++)
		{
			mesh.GetBlendShapeFrameVertices(blendShapeIndex, frame, vertices, normals, tangents);
			for (var i = 0; i < vertexCount; i++)
			{
				var magnitude = vertices[i].magnitude;
				sumMagnitude += magnitude;
				if (magnitude > maxMagnitude)
				{
					maxMagnitude = magnitude;
				}
			}
		}

		var scale = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z, 1.0f);
		var epsilon = scale * EmptyEpsilonScale;
		return maxMagnitude < epsilon && sumMagnitude < epsilon * Math.Max(vertexCount, 1);
	}

	public static int FindBlendShapeIndexIgnoreCase(Mesh mesh, string blendShapeName, out string actualName, out bool exactCase)
	{
		actualName = "";
		exactCase = false;
		if (mesh == null || string.IsNullOrEmpty(blendShapeName))
		{
			return -1;
		}

		var caseInsensitiveIndex = -1;
		for (var i = 0; i < mesh.blendShapeCount; i++)
		{
			var name = mesh.GetBlendShapeName(i);
			if (name == blendShapeName)
			{
				actualName = name;
				exactCase = true;
				return i;
			}

			if (caseInsensitiveIndex < 0 && string.Equals(name, blendShapeName, StringComparison.OrdinalIgnoreCase))
			{
				caseInsensitiveIndex = i;
				actualName = name;
			}
		}

		return caseInsensitiveIndex;
	}

	public static BlendShapeDeltas GetLastFrameDeltas(Mesh mesh, int blendShapeIndex)
	{
		var vertexCount = mesh.vertexCount;
		var vertices = new Vector3[vertexCount];
		var normals = new Vector3[vertexCount];
		var tangents = new Vector3[vertexCount];
		var frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
		if (frameCount > 0)
		{
			mesh.GetBlendShapeFrameVertices(blendShapeIndex, frameCount - 1, vertices, normals, tangents);
		}

		return new BlendShapeDeltas(vertices, normals, tangents);
	}

	public static void AddOrReplaceBlendShape(Mesh mesh, string blendShapeName, BlendShapeDeltas deltas, float frameWeight = 100.0f)
	{
		var entries = ReadBlendShapes(mesh);
		var replacement = Replacement(blendShapeName, deltas, frameWeight);

		var replaced = false;
		for (var i = 0; i < entries.Count; i++)
		{
			if (entries[i].Name != blendShapeName)
			{
				continue;
			}

			entries[i] = replacement;
			replaced = true;
			break;
		}

		if (!replaced)
		{
			entries.Add(replacement);
		}

		WriteBlendShapes(mesh, entries);
	}

	public static void AddOrReplaceBlendShapes(Mesh mesh, IReadOnlyDictionary<string, BlendShapeDeltas> replacements)
	{
		if (replacements == null || replacements.Count == 0)
		{
			return;
		}

		var entries = ReadBlendShapes(mesh);
		var remaining = new Dictionary<string, BlendShapeDeltas>(replacements, StringComparer.Ordinal);
		for (var index = 0; index < entries.Count; index++)
		{
			if (!remaining.TryGetValue(entries[index].Name, out var deltas))
			{
				continue;
			}

			entries[index] = Replacement(entries[index].Name, deltas, 100.0f);
			remaining.Remove(entries[index].Name);
		}

		foreach (var replacement in remaining)
		{
			entries.Add(Replacement(replacement.Key, replacement.Value, 100.0f));
		}

		WriteBlendShapes(mesh, entries);
	}

	public static void AddEmptyBlendShape(Mesh mesh, string blendShapeName)
	{
		var vertexCount = mesh.vertexCount;
		AddOrReplaceBlendShape(
			mesh,
			blendShapeName,
			new BlendShapeDeltas(new Vector3[vertexCount], new Vector3[vertexCount], new Vector3[vertexCount]));
	}

	public static void RenameBlendShape(Mesh mesh, string oldName, string newName)
	{
		if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
		{
			throw new InvalidOperationException("BlendShape 名が空です。");
		}

		var entries = ReadBlendShapes(mesh);
		var target = entries.Find(entry => entry.Name == oldName);
		if (target == null)
		{
			throw new InvalidOperationException($"BlendShape が見つかりません: {oldName}");
		}

		if (entries.Exists(entry => entry != target && entry.Name == newName))
		{
			throw new InvalidOperationException($"変更先の BlendShape は既に存在します: {newName}");
		}

		target.Name = newName;
		WriteBlendShapes(mesh, entries);
	}

	public static void RenameBlendShapes(Mesh mesh, IReadOnlyDictionary<string, string> renameMap)
	{
		if (renameMap == null || renameMap.Count == 0)
		{
			return;
		}

		var entries = ReadBlendShapes(mesh);
		foreach (var rename in renameMap)
		{
			var target = entries.Find(entry => entry.Name == rename.Key);
			if (target == null)
			{
				throw new InvalidOperationException($"BlendShape が見つかりません: {rename.Key}");
			}

			if (entries.Exists(entry => entry != target && entry.Name == rename.Value))
			{
				throw new InvalidOperationException($"変更先の BlendShape は既に存在します: {rename.Value}");
			}

			target.Name = rename.Value;
		}

		WriteBlendShapes(mesh, entries);
	}

	public static void AddEmptyBlendShapes(Mesh mesh, IReadOnlyList<string> blendShapeNames)
	{
		if (blendShapeNames == null || blendShapeNames.Count == 0)
		{
			return;
		}

		var entries = ReadBlendShapes(mesh);
		var vertexCount = mesh.vertexCount;
		foreach (var blendShapeName in blendShapeNames)
		{
			if (entries.Exists(entry => entry.Name == blendShapeName))
			{
				continue;
			}

			entries.Add(new BlendShapeEntry
			{
				Name = blendShapeName,
				Frames = new List<BlendShapeFrame>
				{
					new()
					{
						Weight = 100.0f,
						DeltaVertices = new Vector3[vertexCount],
						DeltaNormals = new Vector3[vertexCount],
						DeltaTangents = new Vector3[vertexCount],
					},
				},
			});
		}

		WriteBlendShapes(mesh, entries);
	}

	public static int RemoveBlendShapesIgnoreCase(Mesh mesh, IReadOnlyCollection<string> blendShapeNames)
	{
		if (mesh == null || blendShapeNames == null || blendShapeNames.Count == 0)
		{
			return 0;
		}

		var names = new HashSet<string>(blendShapeNames, StringComparer.OrdinalIgnoreCase);
		var entries = ReadBlendShapes(mesh);
		var remaining = new List<BlendShapeEntry>(entries.Count);
		foreach (var entry in entries)
		{
			if (!names.Contains(entry.Name))
			{
				remaining.Add(entry);
			}
		}

		var removedCount = entries.Count - remaining.Count;
		if (removedCount > 0)
		{
			WriteBlendShapes(mesh, remaining);
		}

		return removedCount;
	}

	public static BlendShapeDeltas ComposeDeltas(Mesh mesh, IReadOnlyList<PerfectSyncSourceAssignment> sources)
	{
		return ComposeDeltas(mesh, sources, null);
	}

	public static BlendShapeDeltas ComposeDeltas(Mesh mesh, IReadOnlyList<PerfectSyncSourceAssignment> sources, IDictionary<string, BlendShapeDeltas> sourceCache)
	{
		var vertexCount = mesh.vertexCount;
		var vertices = new Vector3[vertexCount];
		var normals = new Vector3[vertexCount];
		var tangents = new Vector3[vertexCount];

		foreach (var source in sources)
		{
			if (!source.enabled || string.IsNullOrWhiteSpace(source.blendShapeName))
			{
				continue;
			}

			var index = mesh.GetBlendShapeIndex(source.blendShapeName);
			if (index < 0)
			{
				continue;
			}

			if (sourceCache == null || !sourceCache.TryGetValue(source.blendShapeName, out var deltas))
			{
				deltas = GetLastFrameDeltas(mesh, index);
				sourceCache?.Add(source.blendShapeName, deltas);
			}

			for (var i = 0; i < vertexCount; i++)
			{
				vertices[i] += deltas.Vertices[i] * source.coefficient;
				normals[i] += deltas.Normals[i] * source.coefficient;
				tangents[i] += deltas.Tangents[i] * source.coefficient;
			}
		}

		return new BlendShapeDeltas(vertices, normals, tangents);
	}

	public static PerfectSyncBlendShapeBackup CreateBackup(Mesh mesh)
	{
		var backup = ScriptableObject.CreateInstance<PerfectSyncBlendShapeBackup>();
		backup.meshName = mesh.name;
		backup.vertexCount = mesh.vertexCount;
		var entries = ReadBlendShapes(mesh);
		backup.blendShapes = new BlendShapeBackupEntry[entries.Count];
		for (var i = 0; i < entries.Count; i++)
		{
			var entry = entries[i];
			var backupEntry = new BlendShapeBackupEntry
			{
				name = entry.Name,
				frames = new BlendShapeBackupFrame[entry.Frames.Count],
			};

			for (var frameIndex = 0; frameIndex < entry.Frames.Count; frameIndex++)
			{
				var frame = entry.Frames[frameIndex];
				backupEntry.frames[frameIndex] = new BlendShapeBackupFrame
				{
					weight = frame.Weight,
					deltaVertices = frame.DeltaVertices,
					deltaNormals = frame.DeltaNormals,
					deltaTangents = frame.DeltaTangents,
				};
			}

			backup.blendShapes[i] = backupEntry;
		}

		return backup;
	}

	public static void RestoreBackup(Mesh mesh, PerfectSyncBlendShapeBackup backup)
	{
		if (backup.vertexCount != mesh.vertexCount)
		{
			throw new InvalidOperationException($"Mesh とバックアップの頂点数が一致しません。Mesh: {mesh.vertexCount}、バックアップ: {backup.vertexCount}");
		}

		var entries = new List<BlendShapeEntry>(backup.blendShapes.Length);
		foreach (var backupEntry in backup.blendShapes)
		{
			var frames = new List<BlendShapeFrame>(backupEntry.frames.Length);
			foreach (var backupFrame in backupEntry.frames)
			{
				frames.Add(new BlendShapeFrame
				{
					Weight = backupFrame.weight,
					DeltaVertices = backupFrame.deltaVertices,
					DeltaNormals = backupFrame.deltaNormals,
					DeltaTangents = backupFrame.deltaTangents,
				});
			}

			entries.Add(new BlendShapeEntry
			{
				Name = backupEntry.name,
				Frames = frames,
			});
		}

		WriteBlendShapes(mesh, entries);
	}

	private static List<BlendShapeEntry> ReadBlendShapes(Mesh mesh)
	{
		var entries = new List<BlendShapeEntry>(mesh.blendShapeCount);
		var vertexCount = mesh.vertexCount;
		for (var index = 0; index < mesh.blendShapeCount; index++)
		{
			var frameCount = mesh.GetBlendShapeFrameCount(index);
			var frames = new List<BlendShapeFrame>(frameCount);
			for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
			{
				var vertices = new Vector3[vertexCount];
				var normals = new Vector3[vertexCount];
				var tangents = new Vector3[vertexCount];
				mesh.GetBlendShapeFrameVertices(index, frameIndex, vertices, normals, tangents);
				frames.Add(new BlendShapeFrame
				{
					Weight = mesh.GetBlendShapeFrameWeight(index, frameIndex),
					DeltaVertices = vertices,
					DeltaNormals = normals,
					DeltaTangents = tangents,
				});
			}

			entries.Add(new BlendShapeEntry
			{
				Name = mesh.GetBlendShapeName(index),
				Frames = frames,
			});
		}

		return entries;
	}

	private static void WriteBlendShapes(Mesh mesh, IReadOnlyList<BlendShapeEntry> entries)
	{
		Undo.RegisterCompleteObjectUndo(mesh, "Edit PerfectSync BlendShapes");
		mesh.ClearBlendShapes();
		foreach (var entry in entries)
		{
			foreach (var frame in entry.Frames)
			{
				mesh.AddBlendShapeFrame(entry.Name, frame.Weight, frame.DeltaVertices, frame.DeltaNormals, frame.DeltaTangents);
			}
		}

		EditorUtility.SetDirty(mesh);
	}

	private static BlendShapeEntry Replacement(string name, BlendShapeDeltas deltas, float frameWeight)
	{
		return new BlendShapeEntry
		{
			Name = name,
			Frames = new List<BlendShapeFrame>
			{
				new()
				{
					Weight = frameWeight,
					DeltaVertices = deltas.Vertices,
					DeltaNormals = deltas.Normals,
					DeltaTangents = deltas.Tangents,
				},
			},
		};
	}

	private sealed class BlendShapeEntry
	{
		public string Name = "";
		public List<BlendShapeFrame> Frames = new();
	}

	private sealed class BlendShapeFrame
	{
		public float Weight;
		public Vector3[] DeltaVertices = Array.Empty<Vector3>();
		public Vector3[] DeltaNormals = Array.Empty<Vector3>();
		public Vector3[] DeltaTangents = Array.Empty<Vector3>();
	}
}
}
