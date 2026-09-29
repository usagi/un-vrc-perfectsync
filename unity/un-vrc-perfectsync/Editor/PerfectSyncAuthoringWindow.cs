using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UNVRC.PerfectSync.Editor
{

public sealed class PerfectSyncAuthoringWindow : EditorWindow
{
	private const string LastFaceObjectKey = "UNVRC.PerfectSync.LastFaceObject";
	private const int MaxVisibleSourceRows = 5;
	private const float SourceListHeight = 164.0f;

	private enum UiRefreshScope
	{
		None,
		SourceList,
		Selection,
		All,
	}

	private static readonly string[] DefaultVocabulary =
	{
		"joy",
		"happy",
		"smile",
		"angry",
		"sad",
		"blink",
		"wink",
		"eye",
		"brow",
		"jaw",
		"mouth",
		"lip",
		"tongue",
		"a",
		"i",
		"u",
		"e",
		"o",
		"left",
		"right",
		"vrc.v_",
	};

	private static readonly string[] MatchModeLabels =
	{
		"Contains",
		"Starts",
		"Ends",
		"Exact",
		"Regex",
	};

	[NonSerialized]
	private PerfectSyncSession session;
	private PerfectSyncFilter filter = PerfectSyncFilter.All;
	private PerfectSyncMatchMode matchMode = PerfectSyncMatchMode.Contains;
	private string search = "";
	private string regexError = "";

	private ObjectField targetField;
	private Button rescanButton;
	private Button backupButton;
	private Button restoreButton;
	private Button resetButton;
	private VisualElement bulkActionRow;
	private VisualElement filterRow;
	private ScrollView keyList;
	private VisualElement selectedKeyEditor;
	private VisualElement sourceListContainer;
	private Label statusLine;
	private readonly Dictionary<string, VisualElement> keyRows = new();
	private readonly Dictionary<string, Label> keyNameLabels = new();
	private IVisualElementScheduledItem scheduledUiRefresh;
	private UiRefreshScope pendingUiRefresh;
	private string pendingOldSelectedKey = "";
	private string pendingKeySelection = "";
	private bool hasPendingKeySelection;

	private PerfectSyncSession Session
	{
		get
		{
			if (session == null)
			{
				session = new PerfectSyncSession(DefaultVocabulary);
			}

			return session;
		}
	}

	[MenuItem("Tools/U.N. VRC/PerfectSync Authoring")]
	public static void ShowWindow()
	{
		EnsureEditorAntiAliasing();
		var window = GetWindow<PerfectSyncAuthoringWindow>();
		window.titleContent = new GUIContent("PerfectSync");
		window.minSize = new Vector2(720.0f, 520.0f);
		window.Show();
	}

	private void OnEnable()
	{
		EnsureEditorAntiAliasing();
	}

	private void OnDisable()
	{
		scheduledUiRefresh?.Pause();
		scheduledUiRefresh = null;
		pendingUiRefresh = UiRefreshScope.None;
		hasPendingKeySelection = false;
		session?.OnDisable();
	}

	public void CreateGUI()
	{
		EnsureEditorAntiAliasing();
		_ = Session;
		var contentRoot = new VisualElement();
		contentRoot.style.flexGrow = 1.0f;
		contentRoot.style.paddingLeft = 12.0f;
		contentRoot.style.paddingRight = 12.0f;
		contentRoot.style.paddingTop = 10.0f;
		contentRoot.style.paddingBottom = 8.0f;
		contentRoot.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

		var header = BuildHeader();
		header.style.flexShrink = 0.0f;
		contentRoot.Add(header);

		bulkActionRow = Row();
		bulkActionRow.style.marginTop = 2.0f;
		bulkActionRow.style.marginBottom = 4.0f;
		bulkActionRow.style.flexShrink = 0.0f;
		contentRoot.Add(bulkActionRow);

		filterRow = Row();
		filterRow.style.marginBottom = 6.0f;
		filterRow.style.flexShrink = 0.0f;
		contentRoot.Add(filterRow);

		keyList = new ScrollView(ScrollViewMode.Vertical);
		keyList.style.minHeight = 180.0f;
		keyList.style.flexGrow = 1.0f;
		keyList.style.flexShrink = 1.0f;
		keyList.style.backgroundColor = new Color(0.145f, 0.145f, 0.145f);
		keyList.style.borderTopWidth = 1.0f;
		keyList.style.borderBottomWidth = 1.0f;
		keyList.style.borderLeftWidth = 1.0f;
		keyList.style.borderRightWidth = 1.0f;
		keyList.style.borderTopColor = BorderColor();
		keyList.style.borderBottomColor = BorderColor();
		keyList.style.borderLeftColor = BorderColor();
		keyList.style.borderRightColor = BorderColor();
		keyList.style.paddingTop = 4.0f;
		keyList.style.paddingBottom = 4.0f;
		contentRoot.Add(keyList);

		selectedKeyEditor = new VisualElement();
		selectedKeyEditor.style.marginTop = 8.0f;
		selectedKeyEditor.style.flexShrink = 0.0f;
		contentRoot.Add(selectedKeyEditor);

		statusLine = new Label();
		statusLine.style.color = new Color(0.78f, 0.78f, 0.78f);
		statusLine.style.fontSize = 10.0f;
		statusLine.style.height = 18.0f;
		statusLine.style.flexShrink = 0.0f;
		contentRoot.Add(statusLine);

		RestoreLastFaceObject();
		RefreshAllNow();

		// Attach one fully-built subtree so UI Toolkit never observes a partially-built hierarchy.
		ReplaceChildren(rootVisualElement, contentRoot);
	}

	private VisualElement BuildHeader()
	{
		var header = new VisualElement();
		header.style.paddingBottom = 4.0f;

		var targetRow = Row();
		targetRow.style.height = 26.0f;
		targetRow.style.alignItems = Align.Center;
		targetRow.Add(FixedLabel("Face Object", 86.0f));
		targetField = new ObjectField
		{
			objectType = typeof(GameObject),
			allowSceneObjects = true,
		};
		targetField.style.flexGrow = 1.0f;
		targetField.style.minWidth = 180.0f;
		targetField.style.height = 22.0f;
		targetField.RegisterValueChangedCallback(evt =>
		{
			SetTargetRenderer(ResolveRenderer(evt.newValue as GameObject));
			RequestAllRefresh();
		});
		targetRow.Add(targetField);

		rescanButton = SecondaryButton("Rescan", 68.0f, () =>
		{
			Session.Scan();
			RequestAllRefresh();
		});
		targetRow.Add(rescanButton);

		backupButton = SecondaryButton("Backup...", 78.0f, BackupBlendShapes);
		targetRow.Add(backupButton);

		restoreButton = SecondaryButton("Restore...", 78.0f, RestoreBlendShapes);
		targetRow.Add(restoreButton);

		resetButton = SecondaryButton("Reset Keys...", 86.0f, ConfirmResetAllPerfectSyncKeys);
		resetButton.tooltip = "すべての Perfect Sync Key と割り当てを削除して UNDEFINED に戻す";
		resetButton.style.marginLeft = 8.0f;
		targetRow.Add(resetButton);
		header.Add(targetRow);

		return header;
	}

	private void RefreshAllNow()
	{
		var targetObject = Session.TargetRenderer != null ? Session.TargetRenderer.gameObject : null;
		targetField?.SetValueWithoutNotify(targetObject);
		RebuildUtilityRow();
		RebuildBulkActionRow();
		RebuildFilterRow();
		RebuildKeyList();
		RebuildSelectedKeyEditor();
		UpdateStatusLine();
	}

	private void RequestAllRefresh()
	{
		ScheduleUiRefresh(UiRefreshScope.All);
	}

	private void RequestSelectionRefresh(string oldSelectedKey)
	{
		if (pendingUiRefresh < UiRefreshScope.Selection)
		{
			pendingOldSelectedKey = oldSelectedKey;
		}

		ScheduleUiRefresh(UiRefreshScope.Selection);
	}

	private void RequestSourceListRefresh()
	{
		ScheduleUiRefresh(UiRefreshScope.SourceList);
	}

	private void RequestKeySelection(string keyName)
	{
		pendingKeySelection = keyName;
		hasPendingKeySelection = true;
		ScheduleUiRefresh(UiRefreshScope.Selection);
	}

	private void ScheduleUiRefresh(UiRefreshScope scope)
	{
		if (scope > pendingUiRefresh)
		{
			pendingUiRefresh = scope;
		}

		if (scheduledUiRefresh == null && rootVisualElement?.panel != null)
		{
			// Structural UI changes are deferred until the current input event has finished.
			scheduledUiRefresh = rootVisualElement.schedule.Execute(FlushScheduledUiRefresh);
		}
	}

	private void FlushScheduledUiRefresh()
	{
		scheduledUiRefresh = null;
		if (rootVisualElement?.panel == null)
		{
			pendingUiRefresh = UiRefreshScope.None;
			hasPendingKeySelection = false;
			return;
		}

		var scope = pendingUiRefresh;
		var oldSelectedKey = pendingOldSelectedKey;
		pendingUiRefresh = UiRefreshScope.None;
		pendingOldSelectedKey = "";

		if (hasPendingKeySelection)
		{
			if (string.IsNullOrEmpty(oldSelectedKey))
			{
				oldSelectedKey = Session.SelectedKey;
			}

			var keyName = pendingKeySelection;
			hasPendingKeySelection = false;
			pendingKeySelection = "";
			try
			{
				Session.SelectKey(keyName);
			}
			catch (OperationCanceledException)
			{
				Session.ClearSelection();
			}
			catch (Exception exception)
			{
				EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
			}
		}

		switch (scope)
		{
			case UiRefreshScope.All:
				RefreshAllNow();
				break;
			case UiRefreshScope.Selection:
				RefreshSelectionNow(oldSelectedKey);
				break;
			case UiRefreshScope.SourceList:
				RefreshSourceListNow();
				break;
		}
	}

	private void RebuildUtilityRow()
	{
		var canEditBlendShapes = Session.TargetRenderer != null && Session.TargetRenderer.sharedMesh != null;
		if (rescanButton != null)
		{
			rescanButton.SetEnabled(canEditBlendShapes);
		}

		if (backupButton != null)
		{
			backupButton.SetEnabled(canEditBlendShapes);
		}

		if (restoreButton != null)
		{
			restoreButton.SetEnabled(canEditBlendShapes);
		}

		if (resetButton != null)
		{
			var hasResettableState = canEditBlendShapes && Session.KeyStates.Values.Any(state =>
				state.status != PerfectSyncKeyStatus.Undefined || state.sources.Count > 0);
			resetButton.style.display = hasResettableState ? DisplayStyle.Flex : DisplayStyle.None;
			resetButton.SetEnabled(hasResettableState);
		}
	}

	private void RebuildBulkActionRow()
	{
		if (bulkActionRow == null)
		{
			return;
		}

		var content = Row();
		content.style.alignItems = Align.Center;
		content.style.flexGrow = 1.0f;
		var hasTarget = Session.TargetRenderer != null && Session.TargetRenderer.sharedMesh != null;
		bulkActionRow.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
		if (!hasTarget)
		{
			ReplaceChildren(bulkActionRow, content);
			return;
		}

		bulkActionRow.style.alignItems = Align.Center;
		var label = FixedLabel("Next", 86.0f);
		label.style.marginRight = 10.0f;
		content.Add(label);
		var caseCount = Session.KeyStates.Values.Count(IsCaseMismatch);
		var undefinedCount = Session.KeyStates.Values.Count(state => state.status == PerfectSyncKeyStatus.Undefined);
		var autoSetupPlan = caseCount == 0 ? Session.AnalyzeAutoSetup() : null;
		var reconfigurePlan = caseCount == 0 && (autoSetupPlan == null || autoSetupPlan.Suggestions.Count == 0)
			? Session.AnalyzeAutoSetup(true)
			: null;
		if (caseCount > 0)
		{
			content.Add(PrimaryButton("Fix Case Names", 132.0f, AutoFixCase));
			content.Add(StatusText($"Case {caseCount}", CaseColor()));
		}
		else if (autoSetupPlan != null && autoSetupPlan.Suggestions.Count > 0)
		{
			var plan = autoSetupPlan;
			content.Add(PrimaryButton($"Auto Setup ({plan.Suggestions.Count})", 142.0f, () => ConfirmAutoSetup(plan)));
			content.Add(StatusText($"{plan.Suggestions.Count} keys found", AssignedColor()));
		}
		else if (undefinedCount > 0)
		{
			var nextStep = new Label("Todo の Key を選択して、元になる表情を割り当てます。");
			nextStep.style.color = new Color(0.82f, 0.82f, 0.82f);
			nextStep.style.fontSize = 10.0f;
			nextStep.style.flexGrow = 1.0f;
			nextStep.style.minWidth = 100.0f;
			nextStep.style.unityTextAlign = TextAnchor.MiddleLeft;
			content.Add(nextStep);
			if (reconfigurePlan != null && reconfigurePlan.Suggestions.Count > 0)
			{
				var plan = reconfigurePlan;
				content.Add(SecondaryButton("Re-run Auto Setup...", 158.0f, () => ConfirmAutoSetupReconfigure(plan)));
			}

			content.Add(SecondaryButton("Create Empty Keys...", 142.0f, () => AutoGenerateUndefined(undefinedCount)));
		}
		else if (reconfigurePlan != null && reconfigurePlan.Suggestions.Count > 0)
		{
			var plan = reconfigurePlan;
			content.Add(SecondaryButton("Re-run Auto Setup...", 158.0f, () => ConfirmAutoSetupReconfigure(plan)));
			content.Add(StatusText($"{plan.OverwriteCount} detected keys", AssignedColor()));
		}

		if (caseCount == 0
			&& undefinedCount == 0
			&& (reconfigurePlan == null || reconfigurePlan.Suggestions.Count == 0))
		{
			content.Add(StatusText("No blocking issues", AssignedColor()));
		}

		var hintText = caseCount > 0
			? "名前だけを正式名に揃えます。動きは変わりません。"
			: autoSetupPlan != null && autoSetupPlan.Suggestions.Count > 0
				? "既存の BlendShape を解析し、設定できる Key をまとめて作成します。"
				: undefinedCount == 0 && reconfigurePlan != null && reconfigurePlan.Suggestions.Count > 0
					? "検出済みの Key だけを再設定できます。"
				: undefinedCount > 0
					? ""
					: "設定済みです。必要に応じて Backup... で保存できます。";
		if (!string.IsNullOrEmpty(hintText))
		{
			var hint = new Label(hintText);
			hint.style.color = MutedTextColor();
			hint.style.fontSize = 10.0f;
			hint.style.marginLeft = 6.0f;
			hint.style.unityTextAlign = TextAnchor.MiddleLeft;
			content.Add(hint);
		}
		ReplaceChildren(bulkActionRow, content);
	}

	private void RebuildFilterRow()
	{
		var content = Row();
		content.style.flexGrow = 1.0f;
		var hasTarget = Session.TargetRenderer != null && Session.TargetRenderer.sharedMesh != null;
		filterRow.style.display = hasTarget ? DisplayStyle.Flex : DisplayStyle.None;
		if (!hasTarget)
		{
			ReplaceChildren(filterRow, content);
			return;
		}

		var states = Session.KeyStates.Values.ToList();
		var todo = states.Count(state => state.status is PerfectSyncKeyStatus.Undefined or PerfectSyncKeyStatus.Empty or PerfectSyncKeyStatus.Warning);
		var assigned = states.Count(state => state.status is PerfectSyncKeyStatus.Assigned or PerfectSyncKeyStatus.Existing);
		var todoColor = TodoAggregateColor(states);
		var assignedColor = assigned > 0 ? AssignedColor() : DefaultButtonColor();
		var allColor = todo > 0 ? todoColor : assigned > 0 ? new Color(0.28f, 0.42f, 0.33f) : DefaultButtonColor();
		content.Add(FilterButton("All", "52", PerfectSyncFilter.All, 86.0f, allColor));
		content.Add(FilterButton("Todo", todo.ToString(), PerfectSyncFilter.Todo, 96.0f, todo > 0 ? todoColor : DefaultButtonColor()));
		content.Add(FilterButton("Assigned", assigned.ToString(), PerfectSyncFilter.Assigned, 124.0f, assignedColor));
		content.Add(Spacer());
		content.Add(SummaryLabel());
		ReplaceChildren(filterRow, content);
	}

	private Button FilterButton(string label, string count, PerfectSyncFilter value, float width, Color accentColor)
	{
		var button = new Button(() =>
		{
			filter = value;
			RequestAllRefresh();
		})
		{
			text = $"{label}  {count}",
		};
		button.style.width = width;
		button.style.height = 28.0f;
		button.style.marginRight = 6.0f;
		button.style.marginBottom = 0.0f;
		button.style.backgroundColor = filter == value ? new Color(0.19f, 0.21f, 0.21f) : Color.clear;
		button.style.borderBottomWidth = filter == value ? 3.0f : 1.0f;
		button.style.borderBottomColor = filter == value ? accentColor : BorderColor();
		button.style.borderTopWidth = 0.0f;
		button.style.borderLeftWidth = 0.0f;
		button.style.borderRightWidth = 0.0f;
		if (filter == value)
		{
			button.style.unityFontStyleAndWeight = FontStyle.Bold;
		}

		return button;
	}

	private static VisualElement StatusText(string text, Color color)
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		row.style.marginLeft = 4.0f;
		row.style.marginRight = 10.0f;
		var dot = new VisualElement();
		dot.style.width = 8.0f;
		dot.style.height = 8.0f;
		dot.style.marginRight = 5.0f;
		dot.style.backgroundColor = color;
		row.Add(dot);
		var label = new Label(text);
		label.style.color = new Color(0.82f, 0.82f, 0.82f);
		label.style.fontSize = 11.0f;
		row.Add(label);
		return row;
	}

	private static Label CaseNameLabel(string text)
	{
		var label = new Label(text);
		label.style.height = 22.0f;
		label.style.minWidth = 180.0f;
		label.style.marginRight = 12.0f;
		label.style.paddingLeft = 8.0f;
		label.style.paddingRight = 8.0f;
		label.style.unityTextAlign = TextAnchor.MiddleLeft;
		label.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
		label.style.color = new Color(0.84f, 0.84f, 0.84f);
		return label;
	}

	private void RebuildKeyList()
	{
		var content = new VisualElement();
		keyRows.Clear();
		keyNameLabels.Clear();
		if (Session.TargetRenderer == null || Session.TargetRenderer.sharedMesh == null)
		{
			keyList.style.height = StyleKeyword.Auto;
			keyList.style.flexGrow = 1.0f;
			content.Add(EmptyState("Face Object を指定", "SkinnedMeshRenderer を持つ顔オブジェクトを指定すると、52個の Perfect Sync Key を自動で読み込みます。"));
			ReplaceChildren(keyList, content);
			return;
		}

		keyList.style.height = StyleKeyword.Auto;
		keyList.style.flexGrow = 1.0f;
		content.Add(BuildKeyListHeader());
		foreach (var keyName in PerfectSyncKeys.Names)
		{
			var state = Session.KeyStates[keyName];
			if (!ShouldShow(state))
			{
				continue;
			}

			var row = BuildKeyRow(state);
			keyRows[state.keyName] = row;
			content.Add(row);
		}

		ReplaceChildren(keyList, content);
	}

	private VisualElement BuildKeyRow(PerfectSyncKeyState state)
	{
		var row = Row();
		row.style.height = 26.0f;
		row.style.alignItems = Align.Center;
		row.style.paddingLeft = 8.0f;
		row.style.paddingRight = 8.0f;
		row.style.marginLeft = 4.0f;
		row.style.marginRight = 4.0f;
		row.style.marginBottom = 1.0f;
		ApplyKeyRowSelectionStyle(row, state.keyName == Session.SelectedKey);
		row.style.flexGrow = 1.0f;
		row.Add(StatusMarkLabel(state));
		row.RegisterCallback<ClickEvent>(_ => RequestKeySelection(state.keyName));
		var keyNameLabel = KeyNameLabel(state.keyName);
		ApplyKeyNameSelectionStyle(keyNameLabel, state.keyName == Session.SelectedKey);
		keyNameLabels[state.keyName] = keyNameLabel;
		row.Add(keyNameLabel);
		var status = new Label(StatusLabel(state));
		status.style.color = MutedTextColor();
		status.style.width = 170.0f;
		status.style.minWidth = 120.0f;
		row.Add(status);
		var hint = new Label(PerfectSyncKeys.Hint(state.keyName));
		hint.style.color = new Color(0.68f, 0.68f, 0.68f);
		hint.style.flexGrow = 1.0f;
		row.Add(hint);
		return row;
	}

	private void RefreshSelectionNow(string oldSelectedKey)
	{
		if (!string.IsNullOrEmpty(oldSelectedKey) && keyRows.TryGetValue(oldSelectedKey, out var oldRow))
		{
			ApplyKeyRowSelectionStyle(oldRow, false);
		}

		if (!string.IsNullOrEmpty(oldSelectedKey) && keyNameLabels.TryGetValue(oldSelectedKey, out var oldLabel))
		{
			ApplyKeyNameSelectionStyle(oldLabel, false);
		}

		if (!string.IsNullOrEmpty(Session.SelectedKey) && keyRows.TryGetValue(Session.SelectedKey, out var newRow))
		{
			ApplyKeyRowSelectionStyle(newRow, true);
		}

		if (!string.IsNullOrEmpty(Session.SelectedKey) && keyNameLabels.TryGetValue(Session.SelectedKey, out var newLabel))
		{
			ApplyKeyNameSelectionStyle(newLabel, true);
		}

		RebuildSelectedKeyEditor();
		UpdateStatusLine();
	}

	private void RebuildSelectedKeyEditor()
	{
		sourceListContainer = null;
		selectedKeyEditor.style.display = DisplayStyle.Flex;
		selectedKeyEditor.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
		selectedKeyEditor.style.borderTopWidth = 1.0f;
		selectedKeyEditor.style.borderBottomWidth = 1.0f;
		selectedKeyEditor.style.borderLeftWidth = 1.0f;
		selectedKeyEditor.style.borderRightWidth = 1.0f;
		selectedKeyEditor.style.borderTopColor = BorderColor();
		selectedKeyEditor.style.borderBottomColor = BorderColor();
		selectedKeyEditor.style.borderLeftColor = BorderColor();
		selectedKeyEditor.style.borderRightColor = BorderColor();
		selectedKeyEditor.style.paddingLeft = 12.0f;
		selectedKeyEditor.style.paddingRight = 12.0f;
		selectedKeyEditor.style.paddingTop = 10.0f;
		selectedKeyEditor.style.paddingBottom = 10.0f;
		if (Session.TargetRenderer == null || Session.TargetRenderer.sharedMesh == null)
		{
			selectedKeyEditor.style.display = DisplayStyle.None;
			selectedKeyEditor.Clear();
			return;
		}

		if (string.IsNullOrEmpty(Session.SelectedKey))
		{
			selectedKeyEditor.style.display = DisplayStyle.None;
			selectedKeyEditor.Clear();
			return;
		}

		selectedKeyEditor.style.height = StyleKeyword.Auto;
		selectedKeyEditor.style.maxHeight = StyleKeyword.None;
		selectedKeyEditor.style.flexGrow = 1.0f;
		var content = new VisualElement();
		var state = Session.KeyStates[Session.SelectedKey];
		var header = Row();
		header.style.alignItems = Align.Center;
		header.style.marginBottom = 8.0f;
		var title = BoldLabel(Session.SelectedKey);
		title.style.fontSize = 15.0f;
		title.style.marginRight = 12.0f;
		header.Add(title);
		header.Add(StatusChip(StatusChipText(state), state));
		header.Add(Spacer());
		if (state.status != PerfectSyncKeyStatus.Undefined || state.sources.Count > 0 || !string.IsNullOrEmpty(state.generatedSource))
		{
			header.Add(SecondaryButton("Reset Key...", 104.0f, () => ResetSelectedKey(state.keyName)));
		}

		if (state.status == PerfectSyncKeyStatus.Undefined)
		{
			var createEmptyButton = SecondaryButton("Create Empty", 104.0f, CreateEmptyBlendShapeForSelected);
			createEmptyButton.style.marginRight = 8.0f;
			header.Add(createEmptyButton);
		}

		header.Add(CloseButton(CloseSelectedKeyEditor));

		content.Add(header);
		if (IsCaseMismatch(state))
		{
			BuildCaseMismatchEditor(content, state);
			ReplaceChildren(selectedKeyEditor, content);
			return;
		}

		BuildSelectedKeyGuidance(content, state);
		if (!string.IsNullOrEmpty(state.generatedSource))
		{
			var generated = new Label($"Auto Setup: {state.generatedSource} ({state.generatedMethod})");
			generated.style.whiteSpace = WhiteSpace.Normal;
			content.Add(generated);
		}
		BuildAssignmentBrowser(content, state);
		BuildAssignedSources(content, state);
		BuildVocabulary(content);
		ReplaceChildren(selectedKeyEditor, content);
	}

	private void CloseSelectedKeyEditor()
	{
		var oldSelectedKey = Session.SelectedKey;
		Session.ClearSelection();
		RequestSelectionRefresh(oldSelectedKey);
	}

	private void ResetSelectedKey(string keyName)
	{
		if (!EditorUtility.DisplayDialog("Reset Perfect Sync Key",
			$"{keyName} の BlendShape と割り当て・生成情報を削除し、UNDEFINED に戻します。\n他の Key と元の表情は残します。形状を保存する場合は先に Backup を実行してください。",
			"この Key をリセット", "キャンセル"))
		{
			return;
		}

		try
		{
			Session.ResetKey(keyName);
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void BuildSelectedKeyGuidance(VisualElement parent, PerfectSyncKeyState state)
	{
		var text = state.status switch
		{
			PerfectSyncKeyStatus.Assigned when !string.IsNullOrEmpty(state.generatedSource) => "Auto Setup で合成・左右分割した形状です。Reset Key で解除できます。下の候補を割り当てると、生成した形状を置き換えます。",
			PerfectSyncKeyStatus.Undefined => "この Key の BlendShape はまだありません。元になる表情を割り当てるか、必要な場合だけ空の BlendShape を作成します。",
			PerfectSyncKeyStatus.Empty => "空の BlendShape があります。下の候補から元になる表情を選ぶと、この Key の動きを作成できます。",
			PerfectSyncKeyStatus.Existing => "同名の BlendShape があります。候補の割り当てで置き換えるか、Reset Key でこの Key だけ解除できます。",
			PerfectSyncKeyStatus.Assigned => "割り当て済みです。元になる表情の追加や係数の調整ができます。",
			_ => "",
		};
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		var strip = new VisualElement();
		strip.style.flexDirection = FlexDirection.Row;
		strip.style.alignItems = Align.Center;
		strip.style.minHeight = 24.0f;
		strip.style.marginBottom = 6.0f;
		strip.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);
		var bar = new VisualElement();
		bar.style.width = 4.0f;
		bar.style.height = 18.0f;
		bar.style.marginRight = 8.0f;
		bar.style.backgroundColor = StatusColor(state);
		strip.Add(bar);
		strip.style.paddingRight = 8.0f;
		strip.style.paddingTop = 4.0f;
		strip.style.paddingBottom = 4.0f;
		var label = new Label(text);
		label.style.color = new Color(0.78f, 0.78f, 0.78f);
		label.style.fontSize = 10.0f;
		label.style.flexGrow = 1.0f;
		strip.Add(label);
		parent.Add(strip);
	}

	private void BuildCaseMismatchEditor(VisualElement parent, PerfectSyncKeyState state)
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		row.style.marginTop = 2.0f;
		row.style.marginBottom = 2.0f;
		row.Add(FixedLabel("Current", 72.0f));
		row.Add(CaseNameLabel(state.meshBlendShapeName));
		row.Add(FixedLabel("Canonical", 78.0f));
		row.Add(CaseNameLabel(state.keyName));
		row.Add(Spacer());
		row.Add(CommandButton("Rename", 92.0f, () => FixCaseForSelected(state)));
		parent.Add(row);

		var detail = new Label("既存の BlendShape 名だけを正式名に修正します。形状と割り当ては変更しません。");
		detail.style.color = MutedTextColor();
		detail.style.fontSize = 10.0f;
		parent.Add(detail);
	}

	private void BuildVocabulary(VisualElement parent)
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		row.style.flexWrap = Wrap.Wrap;
		row.style.marginBottom = 4.0f;
		var title = SectionLabel("Quick Search");
		title.style.marginRight = 8.0f;
		row.Add(title);
		foreach (var vocabularyLabel in Session.Vocabulary.ToList())
		{
			var vocabulary = vocabularyLabel;
			row.Add(ChipButton(vocabulary, () =>
			{
				search = vocabulary;
				RequestAllRefresh();
			}));
		}

		var addButton = SecondaryButton("Add Current Search", 136.0f, () =>
		{
			var value = search.Trim();
			if (value.Length > 0 && !Session.Vocabulary.Contains(value, StringComparer.OrdinalIgnoreCase))
			{
				Session.Vocabulary.Add(value);
				Session.SaveVocabulary();
				RequestAllRefresh();
			}
		});
		addButton.SetEnabled(!string.IsNullOrWhiteSpace(search));
		row.Add(addButton);
		parent.Add(row);
	}

	private void BuildAssignmentBrowser(VisualElement parent, PerfectSyncKeyState state)
	{
		var header = Row();
		header.style.alignItems = Align.Center;
		var title = SectionLabel("Assign from BlendShapes");
		title.style.marginRight = 8.0f;
		header.Add(title);
		header.Add(Spacer());
		var assignedCount = state.sources.Count(source => source.enabled);
		var assignedLabel = new Label(assignedCount == 0 ? "No sources assigned" : $"{assignedCount} source(s) assigned");
		assignedLabel.style.color = MutedTextColor();
		assignedLabel.style.fontSize = 10.0f;
		header.Add(assignedLabel);
		parent.Add(header);

		BuildSearch(parent);
		sourceListContainer = new VisualElement();
		sourceListContainer.style.flexShrink = 0.0f;
		parent.Add(sourceListContainer);
		RebuildSourceList(state);
	}

	private void BuildSearch(VisualElement parent)
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		row.style.marginBottom = 4.0f;
		row.Add(FixedLabel("Search", 92.0f));
		var searchField = new TextField
		{
			value = search,
		};
		searchField.style.flexGrow = 1.0f;
		searchField.style.flexShrink = 1.0f;
		searchField.style.minWidth = 0.0f;
		Button clearButton = null;
		searchField.RegisterValueChangedCallback(evt =>
		{
			search = evt.newValue ?? "";
			clearButton?.SetEnabled(!string.IsNullOrEmpty(search));
			RequestSourceListRefresh();
		});
		row.Add(searchField);
		clearButton = SecondaryButton("Clear", 56.0f, () =>
		{
			if (string.IsNullOrEmpty(search))
			{
				return;
			}

			search = "";
			searchField.SetValueWithoutNotify(search);
			clearButton.SetEnabled(false);
			RequestSourceListRefresh();
		});
		clearButton.SetEnabled(!string.IsNullOrEmpty(search));
		clearButton.style.marginLeft = 6.0f;
		clearButton.style.marginRight = 0.0f;
		row.Add(clearButton);
		parent.Add(row);

		var modeRow = Row();
		modeRow.style.alignItems = Align.Center;
		modeRow.style.marginBottom = 4.0f;
		modeRow.Add(FixedLabel("Mode", 92.0f));
		var modeField = new PopupField<string>(MatchModeLabels.ToList(), MatchModeLabel(matchMode));
		modeField.style.width = 128.0f;
		modeField.style.flexShrink = 0.0f;
		modeField.RegisterValueChangedCallback(evt =>
		{
			matchMode = MatchModeFromLabel(evt.newValue);
			RequestSourceListRefresh();
		});
		modeRow.Add(modeField);
		modeRow.Add(Spacer());
		parent.Add(modeRow);

		if (!string.IsNullOrEmpty(regexError))
		{
			var label = new Label($"Regex が不正: {regexError}");
			label.style.fontSize = 10.0f;
			parent.Add(label);
		}
	}

	private void RefreshSourceListNow()
	{
		if (sourceListContainer == null || string.IsNullOrEmpty(Session.SelectedKey))
		{
			return;
		}

		if (!Session.KeyStates.TryGetValue(Session.SelectedKey, out var state))
		{
			return;
		}

		RebuildSourceList(state);
	}

	private void RebuildSourceList(PerfectSyncKeyState state)
	{
		var mesh = Session.TargetRenderer.sharedMesh;
		var assignedNames = new HashSet<string>(
			state.sources.Where(source => source.enabled).Select(source => source.blendShapeName),
			StringComparer.OrdinalIgnoreCase);
		var matches = MatchingBlendShapeNames(mesh)
			.OrderByDescending(assignedNames.Contains)
			.ToList();

		var sourceList = new VisualElement();
		sourceList.style.height = SourceListHeight;
		sourceList.style.flexGrow = 0.0f;
		sourceList.style.flexShrink = 0.0f;
		sourceList.style.marginBottom = 8.0f;

		if (matches.Count == 0)
		{
			var empty = new Label("一致する BlendShape はありません。");
			empty.style.color = MutedTextColor();
			empty.style.fontSize = 11.0f;
			empty.style.marginTop = 8.0f;
			sourceList.Add(empty);
			ReplaceChildren(sourceListContainer, sourceList);
			return;
		}

		sourceList.Add(BuildSourceColumnHeader());

		var visibleCount = Mathf.Min(MaxVisibleSourceRows, matches.Count);
		for (var index = 0; index < visibleCount; index++)
		{
			var row = MakeSourceRow();
			if (index % 2 == 1)
			{
				row.style.backgroundColor = new Color(0.23f, 0.23f, 0.23f);
			}

			BindSourceRow(row, state, matches[index]);
			sourceList.Add(row);
		}

		if (matches.Count > visibleCount)
		{
			var more = new Label($"{matches.Count} 件中 {visibleCount} 件を表示。Search で絞り込み。");
			more.style.color = MutedTextColor();
			more.style.fontSize = 10.0f;
			more.style.marginTop = 4.0f;
			sourceList.Add(more);
		}

		ReplaceChildren(sourceListContainer, sourceList);
	}

	private static VisualElement BuildSourceColumnHeader()
	{
		var header = Row();
		header.style.height = 18.0f;
		header.style.alignItems = Align.Center;
		header.style.marginBottom = 2.0f;
		header.Add(FixedLabel("", 24.0f));
		var source = FixedLabel("Source BlendShape", 300.0f);
		source.style.fontSize = 10.0f;
		source.style.color = MutedTextColor();
		header.Add(source);
		var preview = new Label("Preview (0–100)");
		preview.style.minWidth = 160.0f;
		preview.style.flexGrow = 1.0f;
		preview.style.flexShrink = 1.0f;
		preview.style.fontSize = 10.0f;
		preview.style.color = MutedTextColor();
		header.Add(preview);
		var coefficient = FixedLabel("係数", 64.0f);
		coefficient.style.fontSize = 10.0f;
		coefficient.style.color = MutedTextColor();
		header.Add(coefficient);
		return header;
	}

	private VisualElement MakeSourceRow()
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		var toggle = new Toggle
		{
			name = "toggle",
			tooltip = "この BlendShape を元の表情として割り当てます。",
		};
		toggle.style.width = 24.0f;
		toggle.style.flexShrink = 0.0f;
		row.Add(toggle);

		var nameLabel = FixedLabel("", 300.0f);
		nameLabel.name = "name";
		row.Add(nameLabel);

		var preview = new Slider(0.0f, 100.0f)
		{
			name = "preview",
			tooltip = "Inspector の BlendShape Weight と同じ 0～100 の一時プレビューです。",
		};
		preview.style.minWidth = 160.0f;
		preview.style.flexGrow = 1.0f;
		preview.style.flexShrink = 1.0f;
		row.Add(preview);

		var coefficient = new FloatField
		{
			name = "coefficient",
			tooltip = "Perfect Sync Key の delta に掛ける係数です。",
			isDelayed = true,
		};
		coefficient.style.width = 64.0f;
		coefficient.style.flexShrink = 0.0f;
		row.Add(coefficient);

		toggle.RegisterValueChangedCallback(evt =>
		{
			if (row.userData is not SourceRowContext context)
			{
				return;
			}

			coefficient.SetEnabled(evt.newValue);
			context.owner.UpdateAssignment(context.state, context.sourceName, evt.newValue, coefficient.value);
		});
		coefficient.RegisterValueChangedCallback(evt =>
		{
			if (toggle.value && row.userData is SourceRowContext context)
			{
				context.owner.UpdateAssignment(context.state, context.sourceName, true, evt.newValue);
			}
		});
		preview.RegisterValueChangedCallback(evt =>
		{
			if (row.userData is SourceRowContext context && context.sourceIndex >= 0)
			{
				context.owner.Session.SetPreview(context.sourceName, context.sourceIndex, evt.newValue);
			}
		});

		return row;
	}

	private void BindSourceRow(VisualElement row, PerfectSyncKeyState state, string sourceName)
	{
		var toggle = row.Q<Toggle>("toggle");
		var nameLabel = row.Q<Label>("name");
		var preview = row.Q<Slider>("preview");
		var coefficient = row.Q<FloatField>("coefficient");
		var assignment = state.sources.FirstOrDefault(source => source.blendShapeName == sourceName);
		var sourceIndex = Session.TargetRenderer.sharedMesh.GetBlendShapeIndex(sourceName);

		row.userData = new SourceRowContext(this, state, sourceName, sourceIndex);
		toggle.SetValueWithoutNotify(assignment is { enabled: true });
		nameLabel.text = CompactSourceName(sourceName);
		nameLabel.tooltip = sourceName;
		preview.SetValueWithoutNotify(sourceIndex >= 0 ? Session.PreviewWeight(sourceName, sourceIndex) : 0.0f);
		coefficient.SetValueWithoutNotify(assignment?.coefficient ?? 1.0f);
		coefficient.SetEnabled(toggle.value);
	}

	private void BuildAssignedSources(VisualElement parent, PerfectSyncKeyState state)
	{
		var assigned = state.sources.Where(source => source.enabled).ToList();
		if (assigned.Count == 0)
		{
			return;
		}

		parent.Add(SectionLabel("Assigned"));
		foreach (var source in assigned)
		{
			var row = Row();
			var label = new Label($"{source.blendShapeName} x{source.coefficient:0.###}");
			label.style.flexGrow = 1.0f;
			row.Add(label);
			row.Add(CommandButton("Remove", 72.0f, () =>
			{
				UpdateAssignment(state, source.blendShapeName, false, source.coefficient);
			}));
			parent.Add(row);
		}
	}

	private void UpdateAssignment(PerfectSyncKeyState state, string sourceName, bool enabled, float coefficient)
	{
		try
		{
			Session.UpdateAssignment(state, sourceName, enabled, coefficient);
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void CreateEmptyBlendShapeForSelected()
	{
		if (string.IsNullOrEmpty(Session.SelectedKey))
		{
			return;
		}

		try
		{
			Session.CreateEmptyBlendShape(Session.SelectedKey);
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void FixCaseForSelected(PerfectSyncKeyState state)
	{
		try
		{
			Session.FixBlendShapeCase(state);
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void AutoFixCase()
	{
		try
		{
			Session.FixAllCaseMismatches();
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void ConfirmAutoSetup(PerfectSyncAutoSetupPlan plan)
	{
		if (plan == null || plan.Suggestions.Count == 0)
		{
			return;
		}

		var message =
			$"既存の BlendShape から Perfect Sync Key を {plan.Suggestions.Count} 件設定できます。\n" +
			$"直接割り当て: {plan.DirectCount} 件 / delta 生成: {plan.DerivedCount} 件\n" +
			BuildGeometryValidationSummary(plan) + "\n" +
			BuildAutoSetupPreview(plan) +
			"\n\n通常は未設定の Key だけを設定します。設定済みの Key を含めて再設定する場合は、上書き内容を次の画面で確認できます。";
		var choice = EditorUtility.DisplayDialogComplex(
			"PerfectSync",
			message,
			"未設定のみ",
			"キャンセル",
			"設定済みも上書き...");
		if (choice == 1)
		{
			return;
		}

		var overwriteConfigured = choice == 2;
		var applyPlan = plan;
		if (overwriteConfigured)
		{
			applyPlan = Session.AnalyzeAutoSetup(true);
			if (applyPlan == null || applyPlan.Suggestions.Count == 0)
			{
				EditorUtility.DisplayDialog("PerfectSync", "自動設定できる Key は見つかりませんでした。", "OK");
				return;
			}

			var overwriteMessage =
				$"Perfect Sync Key を {applyPlan.Suggestions.Count} 件設定します。\n" +
				$"設定済みの Key {applyPlan.OverwriteCount} 件を上書きします。\n" +
				$"直接割り当て: {applyPlan.DirectCount} 件 / delta 生成: {applyPlan.DerivedCount} 件\n" +
				BuildGeometryValidationSummary(applyPlan) + "\n" +
				BuildAutoSetupPreview(applyPlan, true) +
				"\n\n上書き対象の delta と元の BlendShape の割り当ては、新しい自動設定に置き換わります。";
			if (!EditorUtility.DisplayDialog(
				"PerfectSync",
				overwriteMessage,
				"上書きして自動設定",
				"キャンセル"))
			{
				return;
			}
		}

		ApplyAutoSetupPlan(applyPlan, overwriteConfigured);
	}

	private void ConfirmAutoSetupReconfigure(PerfectSyncAutoSetupPlan plan)
	{
		if (plan == null || plan.Suggestions.Count == 0)
		{
			EditorUtility.DisplayDialog("PerfectSync", "再設定できる Key は見つかりませんでした。", "OK");
			return;
		}

		var message =
			$"既存の BlendShape を再解析し、Perfect Sync Key を {plan.Suggestions.Count} 件再設定します。\n" +
			$"現在の設定 {plan.OverwriteCount} 件を置き換えます。\n" +
			$"直接割り当て: {plan.DirectCount} 件 / delta 生成: {plan.DerivedCount} 件\n" +
			BuildGeometryValidationSummary(plan) + "\n" +
			BuildAutoSetupPreview(plan, true) +
			"\n\n自動判定できない Key と、その Key の手動設定は変更しません。必要な場合は先に Backup... で保存してください。";
		if (!EditorUtility.DisplayDialog("PerfectSync", message, "再設定", "キャンセル"))
		{
			return;
		}

		ApplyAutoSetupPlan(plan, true);
	}

	private void ApplyAutoSetupPlan(PerfectSyncAutoSetupPlan plan, bool overwriteConfigured)
	{
		try
		{
			Session.ApplyAutoSetup(plan, overwriteConfigured);
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private static string BuildAutoSetupPreview(PerfectSyncAutoSetupPlan plan, bool overwriteFirst = false)
	{
		var suggestions = overwriteFirst
			? plan.Suggestions.OrderByDescending(suggestion => suggestion.OverwritesExisting)
			: plan.Suggestions.AsEnumerable();
		var visible = suggestions.Take(8).ToList();
		var preview = string.Join("\n", visible.Select(suggestion =>
			$"{(suggestion.OverwritesExisting ? "[上書き] " : "")}{suggestion.KeyName} ← {suggestion.SourceName}" +
			(suggestion.Kind == PerfectSyncAutoSetupKind.Direct && Mathf.Abs(suggestion.Coefficient - 1.0f) > 0.001f
				? $" × {suggestion.Coefficient:0.###}"
				: "") +
			(suggestion.Kind == PerfectSyncAutoSetupKind.Derived ? $"（{suggestion.Method}）" : "")));
		if (plan.Suggestions.Count > visible.Count)
		{
			preview += $"\nほか {plan.Suggestions.Count - visible.Count} 件";
		}

		return preview;
	}

	private static string BuildGeometryValidationSummary(PerfectSyncAutoSetupPlan plan)
	{
		return plan.GeometryValidatedCount > 0
			? $"形状確認済み: {plan.GeometryValidatedCount} 件\n"
			: "";
	}

	private void AutoGenerateUndefined(int undefinedCount)
	{
		if (undefinedCount <= 0)
		{
			return;
		}

		if (!EditorUtility.DisplayDialog(
			"PerfectSync",
			$"未作成の Perfect Sync Key {undefinedCount} 件を、空の BlendShape として追加します。\n\n既存の BlendShape の形状は変更されません。",
			"作成",
			"キャンセル"))
		{
			return;
		}

		try
		{
			Session.CreateEmptyBlendShapesForUndefined();
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void ConfirmResetAllPerfectSyncKeys()
	{
		if (!EditorUtility.DisplayDialog(
			"PerfectSync - Reset Keys",
			"52 個の Perfect Sync Key BlendShape と、その割り当てをすべて削除します。\n\n元になった通常の BlendShape は削除しません。実行後はすべて UNDEFINED になり、自動設定または手動設定を最初から行えます。",
			"削除内容を確認",
			"キャンセル"))
		{
			return;
		}

		if (!EditorUtility.DisplayDialog(
			"PerfectSync - 最終確認",
			"この操作は編集中の Mesh を変更します。現在の Key の形状を残す場合は、キャンセルして先に Backup... を実行してください。Backup... は割り当て情報を保存しません。\n\nすべての Perfect Sync Key を UNDEFINED に戻しますか？",
			"すべて UNDEFINED に戻す",
			"キャンセル"))
		{
			return;
		}

		try
		{
			Session.ResetAllPerfectSyncKeys();
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private void UpdateStatusLine()
	{
		if (statusLine != null)
		{
			statusLine.text = StatusMessage();
		}
	}

	private void SetTargetRenderer(SkinnedMeshRenderer nextRenderer)
	{
		var currentSession = Session;
		if (nextRenderer == currentSession.TargetRenderer)
		{
			return;
		}

		currentSession.ChangeTarget(nextRenderer);
		SaveLastFaceObject(nextRenderer);
		search = "";
		regexError = "";
	}

	private static SkinnedMeshRenderer ResolveRenderer(GameObject gameObject)
	{
		if (gameObject == null)
		{
			return null;
		}

		return gameObject.GetComponent<SkinnedMeshRenderer>() ?? gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
	}

	private string StatusMessage()
	{
		if (Session.TargetRenderer == null || Session.TargetRenderer.sharedMesh == null)
		{
			return "Face Object を指定してください。";
		}

		if (string.IsNullOrEmpty(Session.SelectedKey))
		{
			var caseCount = Session.KeyStates.Values.Count(IsCaseMismatch);
			if (caseCount > 0)
			{
				return "Fix Case Names で正式名に修正できます。";
			}

			var undefinedCount = Session.KeyStates.Values.Count(state => state.status == PerfectSyncKeyStatus.Undefined);
			return undefinedCount > 0 ? "Todo の Key を選択して、元になる表情を割り当てます。" : "Perfect Sync Key を選択してください。";
		}

		var state = Session.KeyStates[Session.SelectedKey];
		return !string.IsNullOrEmpty(state.warning) ? SelectedWarningText(state) : "";
	}

	private IEnumerable<string> MatchingBlendShapeNames(Mesh mesh)
	{
		regexError = "";
		if (mesh == null)
		{
			yield break;
		}

		for (var i = 0; i < mesh.blendShapeCount; i++)
		{
			var name = mesh.GetBlendShapeName(i);
			if (string.Equals(name, Session.SelectedKey, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (PerfectSyncSearch.Matches(name, search, matchMode, out var error))
			{
				yield return name;
			}
			else if (!string.IsNullOrEmpty(error))
			{
				regexError = error;
			}
		}
	}

	private bool ShouldShow(PerfectSyncKeyState state)
	{
		return filter switch
		{
			PerfectSyncFilter.All => true,
			PerfectSyncFilter.Todo => state.status is PerfectSyncKeyStatus.Undefined or PerfectSyncKeyStatus.Empty or PerfectSyncKeyStatus.Warning,
			PerfectSyncFilter.Assigned => state.status is PerfectSyncKeyStatus.Assigned or PerfectSyncKeyStatus.Existing,
			_ => true,
		};
	}

	private static string StatusLabel(PerfectSyncKeyState state)
	{
		return state.status switch
		{
			PerfectSyncKeyStatus.Assigned when !string.IsNullOrEmpty(state.generatedSource) => $"Auto: {state.generatedSource} ({state.generatedMethod})",
			PerfectSyncKeyStatus.Assigned when state.sources.Count > 0 => string.Join(", ", state.sources.Where(source => source.enabled).Select(source => $"{source.blendShapeName} x{source.coefficient:0.##}")),
			PerfectSyncKeyStatus.Warning when IsCaseMismatch(state) => state.meshBlendShapeName,
			PerfectSyncKeyStatus.Warning when !string.IsNullOrEmpty(state.warning) => state.warning,
			PerfectSyncKeyStatus.Undefined => "Not found",
			PerfectSyncKeyStatus.Empty => "Exists, empty",
			PerfectSyncKeyStatus.Existing => "Exists",
			PerfectSyncKeyStatus.Assigned => "Assigned",
			_ => "",
		};
	}

	private static string StatusChipText(PerfectSyncKeyState state)
	{
		if (IsCaseMismatch(state))
		{
			return "Case";
		}

		return state.status.ToString();
	}

	private static string SelectedWarningText(PerfectSyncKeyState state)
	{
		return IsCaseMismatch(state) ? $"既存の BlendShape が見つかりました。正式名 {state.keyName} に修正するには Rename を押してください。" : state.warning;
	}

	private static string MatchModeLabel(PerfectSyncMatchMode value)
	{
		return value switch
		{
			PerfectSyncMatchMode.StartsWith => "Starts",
			PerfectSyncMatchMode.EndsWith => "Ends",
			PerfectSyncMatchMode.Exact => "Exact",
			PerfectSyncMatchMode.Regex => "Regex",
			_ => "Contains",
		};
	}

	private static PerfectSyncMatchMode MatchModeFromLabel(string value)
	{
		return value switch
		{
			"Starts" => PerfectSyncMatchMode.StartsWith,
			"Ends" => PerfectSyncMatchMode.EndsWith,
			"Exact" => PerfectSyncMatchMode.Exact,
			"Regex" => PerfectSyncMatchMode.Regex,
			_ => PerfectSyncMatchMode.Contains,
		};
	}

	private static bool IsCaseMismatch(PerfectSyncKeyState state)
	{
		return state.status == PerfectSyncKeyStatus.Warning && !string.IsNullOrEmpty(state.meshBlendShapeName);
	}

	private static VisualElement BuildKeyListHeader()
	{
		var row = Row();
		row.style.height = 22.0f;
		row.style.alignItems = Align.Center;
		row.style.paddingLeft = 8.0f;
		row.style.paddingRight = 8.0f;
		row.style.marginLeft = 4.0f;
		row.style.marginRight = 4.0f;
		row.style.marginBottom = 3.0f;
		row.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
		row.Add(HeaderCell("State", 100.0f));
		row.Add(HeaderCell("Perfect Sync Key", 228.0f));
		row.Add(HeaderCell("Detected", 170.0f));
		var detail = HeaderCell("Hint", 0.0f);
		detail.style.flexGrow = 1.0f;
		row.Add(detail);
		return row;
	}

	private static Label HeaderCell(string text, float width)
	{
		var label = FixedLabel(text, width);
		label.style.color = new Color(0.62f, 0.62f, 0.62f);
		label.style.fontSize = 10.0f;
		label.style.unityFontStyleAndWeight = FontStyle.Bold;
		return label;
	}

	private static VisualElement EmptyState(string title, string detail)
	{
		var element = new VisualElement();
		element.style.flexGrow = 1.0f;
		element.style.justifyContent = Justify.Center;
		element.style.paddingLeft = 18.0f;
		element.style.paddingRight = 18.0f;
		element.Add(EmptyTitle(title));
		element.Add(EmptyDetail(detail));
		return element;
	}

	private static VisualElement InlineState(string title, string detail)
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		row.style.height = 38.0f;
		var titleLabel = EmptyTitle(title);
		titleLabel.style.width = 78.0f;
		titleLabel.style.fontSize = 12.0f;
		row.Add(titleLabel);
		var detailLabel = EmptyDetail(detail);
		detailLabel.style.flexGrow = 1.0f;
		row.Add(detailLabel);
		return row;
	}

	private static VisualElement InlineAction(string title, string detail, string buttonText, Action action)
	{
		var row = InlineState(title, detail);
		row.Add(CommandButton(buttonText, 130.0f, action));
		return row;
	}

	private static Label EmptyTitle(string text)
	{
		var label = new Label(text);
		label.style.color = new Color(0.82f, 0.82f, 0.82f);
		label.style.unityFontStyleAndWeight = FontStyle.Bold;
		label.style.fontSize = 13.0f;
		return label;
	}

	private static Label EmptyDetail(string text)
	{
		var label = new Label(text);
		label.style.color = MutedTextColor();
		label.style.fontSize = 11.0f;
		return label;
	}

	private VisualElement SummaryLabel()
	{
		var values = Session.KeyStates.Values.ToList();
		var caseCount = values.Count(IsCaseMismatch);
		var undefinedCount = values.Count(state => state.status == PerfectSyncKeyStatus.Undefined);
		var emptyCount = values.Count(state => state.status == PerfectSyncKeyStatus.Empty);
		var row = Row();
		row.style.alignItems = Align.Center;
		row.Add(SummaryChip("Case", caseCount, caseCount > 0 ? CaseColor() : DefaultButtonColor()));
		row.Add(SummaryChip("Undefined", undefinedCount, undefinedCount > 0 ? UndefinedColor() : DefaultButtonColor()));
		row.Add(SummaryChip("Empty", emptyCount, emptyCount > 0 ? EmptyColor() : DefaultButtonColor()));
		return row;
	}

	private static VisualElement SummaryChip(string labelText, int count, Color color)
	{
		var row = Row();
		row.style.alignItems = Align.Center;
		row.style.marginLeft = 10.0f;
		var dot = new VisualElement();
		dot.style.width = 8.0f;
		dot.style.height = 8.0f;
		dot.style.marginRight = 5.0f;
		dot.style.backgroundColor = color;
		row.Add(dot);
		var label = new Label($"{labelText} {count}");
		label.style.height = 20.0f;
		label.style.unityTextAlign = TextAnchor.MiddleCenter;
		label.style.color = MutedTextColor();
		label.style.fontSize = 10.0f;
		row.Add(label);
		return row;
	}

	private static Label StatusMarkLabel(PerfectSyncKeyState state)
	{
		var label = FixedLabel(StatusMark(state), 92.0f);
		label.style.height = 18.0f;
		label.style.marginRight = 8.0f;
		label.style.unityTextAlign = TextAnchor.MiddleCenter;
		label.style.color = new Color(0.92f, 0.92f, 0.92f);
		label.style.backgroundColor = StatusColor(state);
		label.style.unityFontStyleAndWeight = FontStyle.Bold;
		return label;
	}

	private static Color StatusColor(PerfectSyncKeyState state)
	{
		if (IsCaseMismatch(state))
		{
			return CaseColor();
		}

		return state.status switch
		{
			PerfectSyncKeyStatus.Existing => AssignedColor(),
			PerfectSyncKeyStatus.Assigned => AssignedColor(),
			PerfectSyncKeyStatus.Empty => EmptyColor(),
			PerfectSyncKeyStatus.Warning => new Color(0.62f, 0.36f, 0.16f),
			PerfectSyncKeyStatus.Undefined => UndefinedColor(),
			_ => Color.white,
		};
	}

	private static Color TodoAggregateColor(IReadOnlyCollection<PerfectSyncKeyState> states)
	{
		if (states.Any(state => state.status == PerfectSyncKeyStatus.Undefined))
		{
			return UndefinedColor();
		}

		if (states.Any(IsCaseMismatch) || states.Any(state => state.status == PerfectSyncKeyStatus.Warning))
		{
			return CaseColor();
		}

		if (states.Any(state => state.status == PerfectSyncKeyStatus.Empty))
		{
			return EmptyColor();
		}

		return DefaultButtonColor();
	}

	private static Color DefaultButtonColor()
	{
		return new Color(0.32f, 0.32f, 0.32f);
	}

	private static Color AssignedColor()
	{
		return new Color(0.24f, 0.46f, 0.32f);
	}

	private static Color EmptyColor()
	{
		return new Color(0.64f, 0.52f, 0.14f);
	}

	private static Color UndefinedColor()
	{
		return new Color(0.48f, 0.25f, 0.28f);
	}

	private static Color CaseColor()
	{
		return new Color(0.56f, 0.39f, 0.14f);
	}

	private static string StatusMark(PerfectSyncKeyState state)
	{
		if (IsCaseMismatch(state))
		{
			return "CASE";
		}

		return state.status switch
		{
			PerfectSyncKeyStatus.Existing => "OK",
			PerfectSyncKeyStatus.Assigned => "OK",
			PerfectSyncKeyStatus.Empty => "EMPTY",
			PerfectSyncKeyStatus.Warning => "!",
			PerfectSyncKeyStatus.Undefined => "UNDEFINED",
			_ => "",
		};
	}

	private void BackupBlendShapes()
	{
		var mesh = Session.TargetRenderer.sharedMesh;
		var path = EditorUtility.SaveFilePanelInProject(
			"Backup BlendShapes",
			$"{mesh.name}_BlendShapes_Backup.asset",
			"asset",
			"現在の BlendShape をバックアップとして保存します。");
		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		var backup = PerfectSyncMeshUtility.CreateBackup(mesh);
		AssetDatabase.CreateAsset(backup, path);
		AssetDatabase.SaveAssets();
	}

	private void RestoreBlendShapes()
	{
		var path = EditorUtility.OpenFilePanel("Restore BlendShapes", Application.dataPath, "asset");
		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		var projectPath = FileUtil.GetProjectRelativePath(path);
		var backup = AssetDatabase.LoadAssetAtPath<PerfectSyncBlendShapeBackup>(projectPath);
		if (backup == null)
		{
			EditorUtility.DisplayDialog("PerfectSync", "選択したファイルは、PerfectSync で作成した BlendShape バックアップではありません。", "OK");
			return;
		}

		try
		{
			Session.RestoreBackup(backup);
		}
		catch (Exception exception) when (!(exception is OperationCanceledException))
		{
			EditorUtility.DisplayDialog("PerfectSync", exception.Message, "OK");
		}

		RequestAllRefresh();
	}

	private sealed class SourceRowContext
	{
		public readonly PerfectSyncAuthoringWindow owner;
		public readonly PerfectSyncKeyState state;
		public readonly string sourceName;
		public readonly int sourceIndex;

		public SourceRowContext(PerfectSyncAuthoringWindow owner, PerfectSyncKeyState state, string sourceName, int sourceIndex)
		{
			this.owner = owner;
			this.state = state;
			this.sourceName = sourceName;
			this.sourceIndex = sourceIndex;
		}
	}

	private static VisualElement Row()
	{
		var row = new VisualElement();
		row.style.flexDirection = FlexDirection.Row;
		row.style.marginBottom = 2.0f;
		return row;
	}

	private static Label FixedLabel(string text, float width)
	{
		var label = new Label(text);
		label.style.width = width;
		label.style.flexShrink = 0.0f;
		return label;
	}

	private static string CompactSourceName(string value)
	{
		const int maxLength = 28;
		if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
		{
			return value;
		}

		const int headLength = 16;
		const int tailLength = 9;
		return $"{value.Substring(0, headLength)}...{value.Substring(value.Length - tailLength, tailLength)}";
	}

	private static Label BoldLabel(string text)
	{
		var label = new Label(text);
		label.style.unityFontStyleAndWeight = FontStyle.Bold;
		return label;
	}

	private static Label SectionLabel(string text)
	{
		var label = BoldLabel(text);
		label.style.marginTop = 6.0f;
		label.style.marginBottom = 3.0f;
		label.style.color = new Color(0.78f, 0.78f, 0.78f);
		return label;
	}

	private static Button CommandButton(string text, float width, Action action)
	{
		var button = new Button(action)
		{
			text = text,
		};
		if (width > 0.0f)
		{
			button.style.width = width;
		}

		button.style.height = 24.0f;
		button.style.marginRight = 6.0f;
		button.style.marginBottom = 2.0f;
		return button;
	}

	private static Button PrimaryButton(string text, float width, Action action)
	{
		var button = CommandButton(text, width, action);
		button.style.backgroundColor = new Color(0.38f, 0.38f, 0.38f);
		button.style.unityFontStyleAndWeight = FontStyle.Bold;
		return button;
	}

	private static Button SecondaryButton(string text, float width, Action action)
	{
		var button = CommandButton(text, width, action);
		button.style.height = 22.0f;
		button.style.fontSize = 11.0f;
		button.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
		button.style.marginBottom = 0.0f;
		return button;
	}

	private static Button CloseButton(Action action)
	{
		var button = new Button(action)
		{
			text = "×",
			tooltip = "キー設定を閉じて、表情のプレビューを元に戻す",
		};
		button.style.width = 24.0f;
		button.style.minWidth = 24.0f;
		button.style.height = 24.0f;
		button.style.paddingLeft = 0.0f;
		button.style.paddingRight = 0.0f;
		button.style.paddingTop = 0.0f;
		button.style.paddingBottom = 2.0f;
		button.style.marginLeft = 0.0f;
		button.style.marginRight = 0.0f;
		button.style.marginTop = 0.0f;
		button.style.marginBottom = 0.0f;
		button.style.fontSize = 16.0f;
		button.style.backgroundColor = Color.clear;
		return button;
	}

	private static Label KeyNameLabel(string text)
	{
		var label = new Label(text)
		{
			pickingMode = PickingMode.Position,
		};
		label.style.width = 220.0f;
		label.style.minWidth = 220.0f;
		label.style.height = 22.0f;
		label.style.paddingLeft = 6.0f;
		label.style.unityTextAlign = TextAnchor.MiddleLeft;
		label.style.marginRight = 8.0f;
		return label;
	}

	private static void ApplyKeyRowSelectionStyle(VisualElement row, bool selected)
	{
		row.style.backgroundColor = selected ? new Color(0.23f, 0.27f, 0.29f) : new Color(0.17f, 0.17f, 0.17f);
	}

	private static void ApplyKeyNameSelectionStyle(Label label, bool selected)
	{
		label.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
	}

	private static Button ChipButton(string text, Action action)
	{
		var button = CommandButton(text, 0.0f, action);
		button.style.height = 22.0f;
		button.style.paddingLeft = 10.0f;
		button.style.paddingRight = 10.0f;
		button.style.marginRight = 4.0f;
		return button;
	}

	private static Label StatusChip(string text, PerfectSyncKeyState state)
	{
		var label = new Label(text);
		label.style.height = 20.0f;
		label.style.paddingLeft = 8.0f;
		label.style.paddingRight = 8.0f;
		label.style.unityTextAlign = TextAnchor.MiddleCenter;
		label.style.backgroundColor = StatusColor(state);
		label.style.color = new Color(0.92f, 0.92f, 0.92f);
		label.style.unityFontStyleAndWeight = FontStyle.Bold;
		return label;
	}

	private static VisualElement Spacer()
	{
		var element = new VisualElement();
		element.style.flexGrow = 1.0f;
		return element;
	}

	private static VisualElement Space(float height)
	{
		var element = new VisualElement();
		element.style.height = height;
		return element;
	}

	private static void ReplaceChildren(VisualElement parent, VisualElement content)
	{
		parent.Clear();
		parent.Add(content);
	}

	private static Color BorderColor()
	{
		return new Color(0.28f, 0.28f, 0.28f);
	}

	private static Color MutedTextColor()
	{
		return new Color(0.72f, 0.72f, 0.72f);
	}

	private static void EnsureEditorAntiAliasing()
	{
		if (QualitySettings.antiAliasing < 1)
		{
			QualitySettings.antiAliasing = 2;
		}
	}

	private void RestoreLastFaceObject()
	{
		if (Session.TargetRenderer != null)
		{
			return;
		}

		var serializedId = EditorPrefs.GetString(LastFaceObjectKey, "");
		if (string.IsNullOrWhiteSpace(serializedId))
		{
			return;
		}

		if (!GlobalObjectId.TryParse(serializedId, out var globalObjectId))
		{
			return;
		}

		var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId) as GameObject;
		var renderer = ResolveRenderer(obj);
		if (renderer != null)
		{
			Session.ChangeTarget(renderer);
		}
	}

	private static void SaveLastFaceObject(SkinnedMeshRenderer renderer)
	{
		if (renderer == null)
		{
			EditorPrefs.DeleteKey(LastFaceObjectKey);
			return;
		}

		var globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(renderer.gameObject);
		EditorPrefs.SetString(LastFaceObjectKey, globalObjectId.ToString());
	}
}
}
