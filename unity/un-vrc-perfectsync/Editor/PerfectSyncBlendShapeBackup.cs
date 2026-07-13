using System;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

public sealed class PerfectSyncBlendShapeBackup : ScriptableObject
{
	public string meshName = "";
	public int vertexCount;
	public BlendShapeBackupEntry[] blendShapes = Array.Empty<BlendShapeBackupEntry>();
}

[Serializable]
public sealed class BlendShapeBackupEntry
{
	public string name = "";
	public BlendShapeBackupFrame[] frames = Array.Empty<BlendShapeBackupFrame>();
}

[Serializable]
public sealed class BlendShapeBackupFrame
{
	public float weight;
	public Vector3[] deltaVertices = Array.Empty<Vector3>();
	public Vector3[] deltaNormals = Array.Empty<Vector3>();
	public Vector3[] deltaTangents = Array.Empty<Vector3>();
}
}
