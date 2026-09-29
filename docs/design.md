# Design

この文書は実装上の境界と、変更時に守る性質を記録します。ユーザー向けの操作は [manual.md](manual.md) を参照してください。

## Scope

U.N. VRC PerfectSync は、VRChat アバターの顔 Mesh に Perfect Sync 52 Key を作成する Editor-only の Unity package です。

扱うもの:

- 52 Key の状態検出
- 既存 BlendShape の割り当てと係数付き delta 合成
- 名前、左右、Avatar Descriptor、変形領域に基づく保守的な自動設定
- Key 名の case 修正と空 Key の作成
- working mesh、mapping profile、BlendShape backup

扱わないもの:

- VRCFT、OSC、Animator、Expression Parameters の設定
- runtime tracking
- 顔の形状を推論して新規生成する処理
- ネットワーク API や ML への依存

## Invariants

- Key を選択しただけでは Mesh を変更しない。
- プレビュー値は一時状態とし、Key 設定を閉じるか対象を変えたときに復元する。
- 元の Mesh Asset は直接編集せず、初回編集時に working mesh を作る。
- Reset は canonical な Perfect Sync Key と割り当てだけを削除し、元の表情 BlendShape を残す。
- Case 修正は delta を変更しない。
- 自動設定は低信頼または形状と明確に矛盾する候補を採用しない。
- 自動再設定は検出できた Key だけを置き換え、検出できない手動設定を残す。
- Rescan は変形キャッシュを破棄し、割り当て済みでも出力 Mesh の現状を検査する。
- 合成・左右分割の生成元と生成方法は profile に保存する。通常の source 割り当てとは区別し、手動割り当てで形状を置き換えたら生成情報を消す。
- Key 単位の Reset は対象だけを削除し、残る BlendShape の Weight を名前で引き継ぐ。

## State

- `UNDEFINED`: canonical 名に対応する BlendShape がない。
- `EMPTY`: 対応する BlendShape の delta が実質ゼロ。
- `OK`: 対応する BlendShape がある、または割り当て済み。
- `CASE`: canonical 名と大文字小文字だけが異なる。

## Editing

割り当てた source の最終 frame を使い、次の式で出力 delta を作ります。

```text
outputDelta = sum(sourceDelta * coefficient)
```

対象は deltaVertices、deltaNormals、deltaTangents です。

## Auto Setup

自動設定は canonical Key を source 候補から除外し、既存名、左右ペア、VRC 系名称、Avatar Descriptor を候補生成に使います。Humanoid の両眼を取得できる場合は、顔領域と左右の変形エネルギーも検証します。

左右の合成や分割には境界付き推定を使い、係数を有限な範囲へ制限します。ランドマークを取得できない場合は、名称と Descriptor による保守的な判定へ戻します。

## Persistence

profile の検索は working mesh の一致を優先し、元 Mesh を使う場合は階層パスの一致も必要とします。working mesh を意図的に共有する renderer は同じ profile を参照します。元 Mesh と階層パスが両方同じ別シーンの renderer を区別する永続 ID は、現在の profile にはありません。

renderer ごとの mapping profile と working mesh は、既定で次へ保存します。

```text
Assets/U.N. VRC PerfectSync/
```

Backup は BlendShape の形状だけを保存します。割り当てや検索語句などの profile 情報は保存しません。

## Main Components

- `PerfectSyncAuthoringWindow`: UI とユーザー操作
- `PerfectSyncSession`: 選択、編集、preview、profile の調停
- `PerfectSyncAutoSetup`: 候補生成、形状検証、係数推定
- `PerfectSyncMeshUtility`: Mesh の読み書きと delta 合成
- `PerfectSyncProfileStore`: renderer ごとの永続化

## Release

VCC package の生成方法は [release.md](release.md) を参照してください。
