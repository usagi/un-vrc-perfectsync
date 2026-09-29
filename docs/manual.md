# U.N. VRC PerfectSync Manual

U.N. VRC PerfectSync は、アバターに既にある顔の BlendShape を利用して Perfect Sync 用の52 Key を整える Unity Editor ツールです。

VRCFT、OSC、Animator Controller、Expression Parameters は設定しません。

## 基本操作

1. `Tools > U.N. VRC > PerfectSync Authoring` を開きます。
2. `Face Object` に、顔の `SkinnedMeshRenderer` を持つ GameObject を指定します。
3. `Auto Setup` が表示された場合は、確認画面で候補を確認して実行します。
4. Todo に残った Key を選び、`Assign from BlendShapes` から元になる表情を割り当てます。
5. 顔を確認しながら、必要な場合だけ係数を調整します。

Key の行は全体をクリックできます。`Hint` 列には、その Key で作る表情の目安が表示されます。

## 状態

| State | 意味 | 主な操作 |
| --- | --- | --- |
| `OK` | 対応する BlendShape がある | 必要な場合だけ内容を確認する |
| `UNDEFINED` | 対応する BlendShape がない | 元になる表情を割り当てる |
| `EMPTY` | BlendShape はあるが動きが空 | 元になる表情を割り当てる |
| `CASE` | 名前の大文字小文字だけが正式名と異なる | `Fix Case Names` で名前を修正する |

`All`、`Todo`、`Assigned` は一覧の表示切り替えです。状態を変更するボタンではありません。

## Auto Setup

`Auto Setup` は、既存の BlendShape 名、左右の組、VRC 系の名前、Avatar Descriptor の設定を解析します。Humanoid の両眼を取得できる場合は、変形領域と左右も確認します。

候補は確認画面に表示されます。すべてのモデルや Key を自動判定できるわけではなく、低信頼の候補は採用しません。実行後は表情を目視で確認してください。

- 通常は未設定の Key だけを設定します。
- 確認画面では、検出できた設定済み Key の上書きも選択できます。
- `Re-run Auto Setup...` は既存の BlendShape を再解析します。自動判定できない Key の手動設定は変更しません。

合成・左右分割で作った Key には、生成元と生成方法を一覧および下部に表示します。選択すると生成した Key の形状をプレビューします。候補を手動で割り当てると、生成した形状全体をその割り当てで置き換えます。

旧版で生成済みの Key は生成情報が保存されていないため `Exists` のままですが、下記の `Reset Key...` は利用できます。

## 手動で割り当てる

Key を選ぶと、下部に `Assign from BlendShapes` が表示されます。

- Search: BlendShape 名を絞り込みます。
- Mode: Search の一致方法を変更します。
- Quick Search: よく使う語句を入力します。
- チェック: 元になる BlendShape を割り当てます。
- プレビュー: 元の BlendShape の動きを一時的に確認します。
- 係数: 出力へ合成する倍率です。通常は `1` から始めます。

プレビューの `0..100` は Unity の BlendShape Weight です。係数とは別の値です。

複数の BlendShape を割り当てると、各変形へ係数を掛けて合成します。

```text
出力 = Σ（元の BlendShape の変形 × 係数）
```

Key 設定右上の `×` で設定欄を閉じると、一時的なプレビューを元に戻します。

## 空の Key

通常は元になる表情の割り当てから始めます。動きのない Key が必要な場合だけ、選択中の `Create Empty` または一括の `Create Empty Keys...` を使います。

## Backup と Restore

大きな変更の前は `Backup...` で現在の BlendShape を保存してください。`Restore...` はそのバックアップから BlendShape を復元します。

Backup に含まれるのは BlendShape の形状です。検索語句、割り当て元、係数などの profile 情報は含まれません。

## 自動再設定と初期化

選択中の Key だけやり直す場合は、下部の Key 見出しにある `Reset Key...` を使います。確認後、その Key の BlendShape と割り当て・生成情報を削除して `UNDEFINED` に戻します。他の Key と元の表情、その Weight は残します。Auto Setup の左右分割で作った Key や、割り当て情報のない既存 Key にも使えます。

設定済みの状態でも `Re-run Auto Setup...` から自動設定をやり直せます。手動編集を含めて最初からやり直す場合は、ヘッダーの `Reset Keys...` を使います。

`Reset Keys...` は次の内容を削除し、52 Key をすべて `UNDEFINED` に戻します。

- Perfect Sync の正式名に一致する BlendShape
- Perfect Sync Key の割り当てと係数

元になった通常の表情 BlendShape は削除しません。実行前に二段階の確認があります。現在の Key の形状を残す場合は、先に `Backup...` を実行してください。

## 作成される Asset

初めて Mesh を変更するときに working mesh を作成し、Face Object の `sharedMesh` を差し替えます。元の Mesh Asset は直接編集しません。

working mesh と mapping profile の既定の保存先:

```text
Assets/U.N. VRC PerfectSync/
```

これらはアバターの設定作業に必要な Asset です。不要と確認できるまで削除しないでください。

## 困ったとき

- 外部ツールで Mesh を変更した: `Rescan` を押します。
- `Auto Setup` が表示されない: 安全に判定できる候補がありません。Todo の Key を手動で設定します。
- `CASE` がある: 先に `Fix Case Names` を実行します。
- 設定をやり直したい: `Re-run Auto Setup...` または `Reset Keys...` を使います。
- Face Object が見つからない: 顔の BlendShape を持つ `SkinnedMeshRenderer` の GameObject を指定します。
