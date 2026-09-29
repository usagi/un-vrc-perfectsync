using System;
using System.Collections.Generic;
using UnityEngine;

namespace UNVRC.PerfectSync.Editor
{

internal enum PerfectSyncKeyStatus
{
	Undefined,
	Empty,
	Existing,
	Assigned,
	Warning,
}

internal enum PerfectSyncFilter
{
	All,
	Todo,
	Assigned,
}

internal enum PerfectSyncMatchMode
{
	Contains,
	StartsWith,
	EndsWith,
	Exact,
	Regex,
}

[Serializable]
internal sealed class PerfectSyncSourceAssignment
{
	public string blendShapeName = "";
	public bool enabled = true;
	public float coefficient = 1.0f;
}

internal sealed class PerfectSyncKeyState
{
	public string keyName = "";
	public PerfectSyncKeyStatus status = PerfectSyncKeyStatus.Undefined;
	public string meshBlendShapeName = "";
	public string generatedSource = "";
	public string generatedMethod = "";
	public readonly List<PerfectSyncSourceAssignment> sources = new();
	public string warning = "";
}

internal sealed class PerfectSyncPreviewState
{
	public string blendShapeName = "";
	public float originalWeight;
	public float previewWeight;
	public bool hadPrefabOverride;
}

internal readonly struct BlendShapeDeltas
{
	public BlendShapeDeltas(Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
	{
		Vertices = vertices;
		Normals = normals;
		Tangents = tangents;
	}

	public Vector3[] Vertices { get; }
	public Vector3[] Normals { get; }
	public Vector3[] Tangents { get; }
}
}
