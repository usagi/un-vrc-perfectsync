using System;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

public sealed class PerfectSyncMappingProfile : ScriptableObject
{
	public int profileVersion = 1;
	public string targetMeshGuid = "";
	public string workingMeshGuid = "";
	public string targetRendererPath = "";
	public Mesh targetMesh;
	public Mesh workingMesh;
	public PerfectSyncKeyProfile[] keys = Array.Empty<PerfectSyncKeyProfile>();
	public string[] vocabulary = Array.Empty<string>();
	public string createdAt = "";
	public string updatedAt = "";
}

[Serializable]
public sealed class PerfectSyncKeyProfile
{
	public string keyName = "";
	public bool ignored;
	public PerfectSyncSourceProfile[] sources = Array.Empty<PerfectSyncSourceProfile>();
	public string notes = "";
}

[Serializable]
public sealed class PerfectSyncSourceProfile
{
	public string blendShapeName = "";
	public float coefficient = 1.0f;
	public bool enabled = true;
}
}
