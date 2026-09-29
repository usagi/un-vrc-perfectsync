using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UNVRC.PerfectSync.Editor
{
public static class SessionRegression
{
	public static void Run()
	{
		var failures = new List<string>();
		Check("Rescan uses current source geometry", failures, (session, renderer) =>
		{
			var state = session.KeyStates["JawOpen"];
			session.UpdateAssignment(state, "Source", true, 1.0f);
			PerfectSyncMeshUtility.AddOrReplaceBlendShape(renderer.sharedMesh, "Source", Deltas(2.0f));
			session.Scan();
			session.UpdateAssignment(state, "Source", true, 0.5f);
			var actual = PerfectSyncMeshUtility.GetLastFrameDeltas(renderer.sharedMesh, renderer.sharedMesh.GetBlendShapeIndex("JawOpen"));
			Assert(Mathf.Approximately(actual.Vertices[0].x, 1.0f), "stale source delta used");
		});
		Check("Missing output is not assigned", failures, (session, renderer) =>
		{
			var state = session.KeyStates["JawOpen"];
			session.UpdateAssignment(state, "Source", true, 1.0f);
			PerfectSyncMeshUtility.RemoveBlendShapesIgnoreCase(renderer.sharedMesh, new[] { "JawOpen" });
			session.Scan();
			Assert(state.status == PerfectSyncKeyStatus.Undefined, "missing output reported OK");
			Assert(state.sources.Count == 1, "assignment lost");
		});
		Check("Assigned output still detects case and empty geometry", failures, (session, renderer) =>
		{
			var state = session.KeyStates["JawOpen"];
			session.UpdateAssignment(state, "Source", true, 1.0f);
			PerfectSyncMeshUtility.RenameBlendShape(renderer.sharedMesh, "JawOpen", "JAWOPEN");
			session.Scan();
			Assert(state.status == PerfectSyncKeyStatus.Warning && state.meshBlendShapeName == "JAWOPEN", "case mismatch hidden");
			session.FixBlendShapeCase(state);
			Assert(state.status == PerfectSyncKeyStatus.Assigned, "case repair failed");
			session.UpdateAssignment(state, "Source", true, 0.0f);
			Assert(state.status == PerfectSyncKeyStatus.Empty, "empty output reported OK");
		});
		Check("Separate renderers do not adopt original mesh profile", failures, (session, renderer) =>
		{
			var original = renderer.sharedMesh;
			session.UpdateAssignment(session.KeyStates["JawOpen"], "Source", true, 1.0f);
			var other = new GameObject("Other_" + Guid.NewGuid().ToString("N"));
			PerfectSyncMappingProfile otherProfile = null;
			try
			{
				var otherRenderer = other.AddComponent<SkinnedMeshRenderer>();
				otherRenderer.sharedMesh = original;
				otherProfile = PerfectSyncProfileStore.LoadOrCreateProfile(otherRenderer, Array.Empty<string>());
				Assert(otherProfile != session.Profile, "another renderer's profile reused");
				Assert(PerfectSyncProfileStore.LoadOrCreateProfile(renderer, Array.Empty<string>()) == session.Profile, "working mesh profile not recovered");
			}
			finally
			{
				if (otherProfile != null && otherProfile != session.Profile) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(otherProfile));
				UnityEngine.Object.DestroyImmediate(other);
			}
		});
		Check("Derived blink persists and can be reset individually", failures, (session, renderer) =>
		{
			var plan = new PerfectSyncAutoSetupPlan();
			foreach (var key in new[] { "EyeBlinkLeft", "EyeBlinkRight" })
			{
				plan.Suggestions.Add(new PerfectSyncAutoSetupSuggestion
				{
					KeyName = key, SourceName = "Source", Method = "左右分割",
					Kind = PerfectSyncAutoSetupKind.Derived, Deltas = Deltas(0.5f),
				});
			}
			Assert(session.ApplyAutoSetup(plan) == 2, "derived setup failed");
			session.ChangeTarget(renderer);
			var left = session.KeyStates["EyeBlinkLeft"];
			Assert(left.status == PerfectSyncKeyStatus.Assigned && left.generatedSource == "Source" && left.generatedMethod == "左右分割", "generation metadata not restored");
			session.SelectKey("EyeBlinkLeft");
			Assert(Mathf.Approximately(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("EyeBlinkLeft")), 100), "derived output not previewed");
			AssertSelectedEditor(session, true);
			session.ClearSelection();
			renderer.SetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("Source"), 12);
			renderer.SetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("EyeBlinkRight"), 37);
			session.ResetKey("EyeBlinkLeft");
			Assert(left.status == PerfectSyncKeyStatus.Undefined && left.generatedSource == "", "reset retained generated key");
			Assert(session.KeyStates["EyeBlinkRight"].status == PerfectSyncKeyStatus.Assigned, "reset removed other key");
			Assert(Mathf.Approximately(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("EyeBlinkRight")), 37), "other key weight shifted");
			Assert(Mathf.Approximately(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("Source")), 12), "source weight changed");
			session.CreateEmptyBlendShape("EyeBlinkLeft");
			Assert(Mathf.Approximately(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("EyeBlinkLeft")), 0), "new key inherits removed weight slot");
			session.UpdateAssignment(session.KeyStates["EyeBlinkRight"], "Source", true, 0.8f);
			Assert(session.KeyStates["EyeBlinkRight"].generatedSource == "", "manual replacement retains stale provenance");
		});
		Check("Legacy existing key has individual reset", failures, (session, renderer) =>
		{
			PerfectSyncMeshUtility.AddOrReplaceBlendShape(renderer.sharedMesh, "EyeBlinkLeft", Deltas(1));
			session.Scan();
			session.SelectKey("EyeBlinkLeft");
			Assert(session.KeyStates["EyeBlinkLeft"].status == PerfectSyncKeyStatus.Existing, "legacy state changed");
			AssertSelectedEditor(session, false);
			session.ResetKey("EyeBlinkLeft");
			Assert(session.KeyStates["EyeBlinkLeft"].status == PerfectSyncKeyStatus.Undefined, "legacy key not reset");
			Assert(renderer.sharedMesh.GetBlendShapeIndex("Source") >= 0, "source removed");
		});
		Check("Changed mesh does not reload old profile by hierarchy alone", failures, (session, renderer) =>
		{
			var replacement = new Mesh { vertices = new[] { Vector3.zero } };
			PerfectSyncMappingProfile profile = null;
			try
			{
				renderer.sharedMesh = replacement;
				profile = PerfectSyncProfileStore.LoadOrCreateProfile(renderer, Array.Empty<string>());
				Assert(profile != session.Profile, "old profile reused after mesh replacement");
			}
			finally
			{
				if (profile != null && profile != session.Profile) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(profile));
				UnityEngine.Object.DestroyImmediate(replacement);
			}
		});
		if (failures.Count > 0) throw new Exception(string.Join("\n", failures));
		Debug.Log("PERFECTSYNC_REGRESSION: all passed");
	}

	private static void Check(string name, List<string> failures, Action<PerfectSyncSession, SkinnedMeshRenderer> test)
	{
		var obj = new GameObject("Regression_" + Guid.NewGuid().ToString("N"));
		var mesh = new Mesh { vertices = new[] { Vector3.zero } };
		mesh.AddBlendShapeFrame("Source", 100.0f, new[] { Vector3.right }, new Vector3[1], new Vector3[1]);
		var renderer = obj.AddComponent<SkinnedMeshRenderer>();
		renderer.sharedMesh = mesh;
		var session = new PerfectSyncSession(Array.Empty<string>());
		try
		{
			session.ChangeTarget(renderer);
			test(session, renderer);
			Debug.Log("PASS: " + name);
		}
		catch (Exception ex)
		{
			failures.Add(name + ": " + ex.Message);
			Debug.Log("FAIL: " + name + ": " + ex.Message);
		}
		finally
		{
			session.OnDisable();
			if (session.Profile != null)
			{
				if (session.Profile.workingMesh != null) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(session.Profile.workingMesh));
				AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(session.Profile));
			}
			UnityEngine.Object.DestroyImmediate(obj);
			UnityEngine.Object.DestroyImmediate(mesh);
		}
	}

	private static BlendShapeDeltas Deltas(float x) => new(new[] { new Vector3(x, 0, 0) }, new Vector3[1], new Vector3[1]);
	private static void AssertSelectedEditor(PerfectSyncSession session, bool generated)
	{
		var window = ScriptableObject.CreateInstance<PerfectSyncAuthoringWindow>();
		try
		{
			var panel = new VisualElement();
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(PerfectSyncAuthoringWindow).GetField("session", flags).SetValue(window, session);
			typeof(PerfectSyncAuthoringWindow).GetField("selectedKeyEditor", flags).SetValue(window, panel);
			typeof(PerfectSyncAuthoringWindow).GetMethod("RebuildSelectedKeyEditor", flags).Invoke(window, null);
			Assert(panel.Query<Button>().ToList().Any(button => button.text == "Reset Key..."), "reset action not visible");
			if (generated) Assert(panel.Query<Label>().ToList().Any(label => label.text.Contains("Auto Setup: Source")), "generated source not visible");
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(window);
		}
	}
	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new Exception(message);
	}
}
}
