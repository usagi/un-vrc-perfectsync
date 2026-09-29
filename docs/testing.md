# Session regression tests

Unity 2022.3 の Editor で、セッション処理と選択 Key の UI 構築を検証します。

```powershell
pwsh ./scripts/Test-Session.ps1 -UnityPath 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
```

実行ごとに `target/session-tests-<id>/` に隔離プロジェクトを作成し、ローカル package を参照します。利用中のアバタープロジェクトは使用しません。テストコードは配布 package の外にあり、隔離プロジェクトだけで asmref によって Editor assembly に追加します。

検証対象:

- 外部変更後の Rescan と再合成
- 出力 Key の欠落・大文字小文字・空形状の検出
- 元 Mesh が同じ別 renderer、および Mesh 差し替え後の profile 照合
- 左右分割の生成情報の保存・読み込み・UI 表示・プレビュー
- Key 個別リセットと、他の Key・元表情・Weight の保持
- 旧版相当の生成情報がない Key の解除導線

成功条件は Unity の終了コード 0 とログの `PERFECTSYNC_REGRESSION: all passed` の両方です。ログとプロジェクトは調査用に残します。UI の画素単位の見た目と実アバターの変形品質は、このバッチテストの検証対象ではありません。
