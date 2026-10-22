# UGUI 核心运行链路

> 对应本地 UGUI 2.0.0 源码。整体职责见 [整体架构](UGUI-整体架构.md)，配套 [框架图](UGUI-架构图.html) 提供“渲染重建”“布局顺序”“输入事件”“遮罩与文字”视图。

## 1. 一个 Image 从启用到参与框架

[Graphic.OnEnable](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L541) 中可以看到主要接入动作：获取所属 Canvas、注册 Graphic、调用 SetAllDirty。后者通过注册更新需求触发调度器的惰性初始化。禁用时则停止参与相关集合，清理 CanvasRenderer 并标记相关布局变化。父节点／Canvas 层级变化也会触发所属关系和 dirty 状态维护。

```text
Image 启用
  → Graphic.OnEnable
  → 缓存／查找 Canvas
  → GraphicRegistry.RegisterGraphicForCanvas
  → SetAllDirty
       ├─ SetLayoutDirty → 布局根进入布局重建队列
       ├─ SetMaterialDirty → 自身进入图形重建队列
       ├─ SetVerticesDirty → 自身进入图形重建队列
       └─ SetRaycastDirty → 维护可射线检测图形集合
```

首次有效的布局或图形注册访问 CanvasUpdateRegistry.instance 时，单例才建立并订阅渲染回调；Graphic.OnEnable 本身没有单独的显式初始化调用。

这说明：**图形在 Canvas 中注册**、**图形需要重建**、**图形可以接受射线检测**是不同状态。GraphicRegistry 的集合与 CanvasUpdateRegistry 的 dirty queue 不能互相替代。

补充：`SetAllDirty` 中包含跳过布局／材质更新的优化标志，并非在所有情况下无条件重做全部工作。

## 2. 属性变化怎样变成可见画面

### 2.1 setter 先记录需求

以 `image.color = newColor` 为例，[Graphic.color](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L156) 在颜色实际改变时调用 `SetVerticesDirty`。颜色属于 UI 顶点数据，因此这里并不要求更换材质。

| 入口 | 记录什么 | 注册到哪里 |
| --- | --- | --- |
| Graphic.SetVerticesDirty | `m_VertsDirty = true` | CanvasUpdateRegistry 图形队列 |
| Graphic.SetMaterialDirty | `m_MaterialDirty = true` | 同一个图形队列 |
| Graphic.SetLayoutDirty | 通过 LayoutRebuilder 寻找有效布局根 | CanvasUpdateRegistry 布局队列 |
| Graphic.SetRaycastDirty | 让 raycastTarget 与射线候选注册状态一致 | GraphicRegistry |

入口：[SetLayoutDirty](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L276)、[SetVerticesDirty](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L293)、[SetMaterialDirty](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L311)、[SetRaycastDirty](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L323)。

两个图形 dirty 标志共享一个 IndexedSet 队列，但重建时独立判断。例如只改颜色通常只需要更新顶点，不必执行材质更新。多次入队会被去重，不过 setter 内的判断、回调或布局根查找并不会因此全部免费。

准确的说法是“合并到后续一次重建处理”，而不是“每个 Graphic 每帧绝对只生成一次网格”。强制刷新、额外渲染回调或特殊组件路径会改变调用次数。

### 2.2 总调度器的阶段顺序

[CanvasUpdateRegistry.PerformUpdate](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/CanvasUpdateRegistry.cs#L162) 由构造器订阅到 `Canvas.willRenderCanvases`。其主体可以概括为以下流程，伪代码省略有效性检查和异常捕获：

```csharp
// 流程摘记，不是可直接替换源码的实现。
CleanInvalidItems();
SortLayoutRootsByHierarchyDepth();
foreach (stage in Prelayout, Layout, PostLayout)
    foreach (element in layoutQueue)
        element.Rebuild(stage);
CompleteAndClearLayoutQueue();

ClipperRegistry.instance.Cull();

foreach (stage in PreRender, LatePreRender)
    foreach (element in graphicQueue)
        element.Rebuild(stage);
CompleteAndClearGraphicQueue();
```

| 阶段 | 队列／参与者 | 实际用途与限制 |
| --- | --- | --- |
| Prelayout | 布局队列中的 ICanvasElement | 提供布局前钩子，具体组件决定是否响应 |
| Layout | 布局队列 | LayoutRebuilder 在这里执行四步布局 |
| PostLayout | 布局队列 | 例如 ScrollRect 在此更新边界／滚动条相关状态 |
| 布局完成后的裁剪 | ClipperRegistry → IClipper | 不属于 CanvasUpdate 枚举中的独立值 |
| PreRender | 图形队列 | 普通 Graphic 在这里更新网格与材质 |
| LatePreRender | 图形队列 | 仍然通知队列元素，普通 Graphic 的 switch 没有对应生成逻辑 |

**阶段枚举存在，不代表每个元素在每个阶段都做工作。** LayoutRebuilder 只响应 Layout，普通 Graphic 主要响应 PreRender。

布局根按祖先数量排序，较浅的根先处理。这是“不同布局根之间的队列顺序”；后面介绍的自底向上／自顶向下，是“一个布局根内部的递归顺序”，两者并不矛盾。

### 2.3 Graphic 的普通网格路径

```text
CanvasUpdateRegistry
 → Graphic.Rebuild(PreRender)
 → 若 m_VertsDirty：UpdateGeometry
 → DoMeshGeneration（普通非 legacy 路径）
 → 子类 OnPopulateMesh(VertexHelper)
 → 同对象的 IMeshModifier.ModifyMesh
 → VertexHelper.FillMesh(workerMesh)
 → CanvasRenderer.SetMesh(workerMesh)
```

Image 的 OnPopulateMesh 根据类型选择四边形、九宫格、平铺或填充生成逻辑；Shadow／Outline 在基础几何生成之后修改顶点。VertexHelper 是顶点／索引等列表的操作封装，最终仍需要填入 Mesh；它不是渲染器。

材质是并列路径：

```text
若 m_MaterialDirty：UpdateMaterial
 → materialForRendering
 → 初始 material 依次经过同对象的 IMaterialModifier.GetModifiedMaterial
 → CanvasRenderer.SetMaterial
 → CanvasRenderer.SetTexture(mainTexture)
```

入口：[Graphic.Rebuild](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L638)、[材质修饰链](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L505)、[DoMeshGeneration](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L694)、[VertexHelper.FillMesh](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Utility/VertexHelper.cs#L207)。

若 CanvasRenderer 不存在或已被 cull，Graphic.Rebuild 会直接返回。dirty 状态不会在这个提前返回中被清掉；恢复可见时，[OnCullingChanged](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L622) 可以让仍有 dirty 状态的图形重新入队。

最终到 CanvasRenderer 的提交只是包内可见的渲染准备，不能等同于“这里已经完成一个 draw call”。

## 3. 布局为什么要走四遍

### 3.1 标记的是有效布局根

[LayoutRebuilder.MarkLayoutForRebuild](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutRebuilder.cs#L170) 从变化的 RectTransform 出发，沿父级中连续有效的 ILayoutGroup 寻找布局根。如果没有走到父布局组，还会检查自身是否有有效 ILayoutController。

找到根后，从 ObjectPool 获取 LayoutRebuilder，绑定该 RectTransform 并尝试注册到布局队列。若重复注册失败则回收；一轮布局完成后也会回收。

因此，调用 SetLayoutDirty 不表示整张 Canvas 必然全部布局，也不保证没有控制器的孤立对象会进入队列。UGUI 会围绕有效布局依赖寻找重建范围，而不是简单地永远寻找最高 Canvas。

### 3.2 四个步骤及方向

[LayoutRebuilder.Rebuild](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutRebuilder.cs#L76) 的顺序是：

| 步骤 | 调用接口 | 递归方向 | 原因 |
| --- | --- | --- | --- |
| 1. 收集横向需求 | CalculateLayoutInputHorizontal | 子 → 父 | 父布局要先知道孩子的宽度需求 |
| 2. 分配横向空间 | SetLayoutHorizontal | 父 → 子 | 孩子先得到父级分配的实际宽度 |
| 3. 收集纵向需求 | CalculateLayoutInputVertical | 子 → 父 | 文字换行等高度需求可能依赖已经确定的宽度 |
| 4. 分配纵向空间 | SetLayoutVertical | 父 → 子 | 按需求和可用空间安排实际高度与位置 |

不是先把横纵需求一起算完再统一写尺寸。宽度影响换行、换行影响高度，是这里先横后纵的重要理由。

这是对布局协议的执行模型；实现会剔除未启用组件，并根据是否存在布局协议跳过不相关子树，不是无条件扫描所有 RectTransform。

在一个节点内，布局控制先执行 ILayoutSelfController，再执行其他 Controller，然后递归孩子。当前代码还对 ScrollRect 的控制调用有专门判断，进一步说明不能仅看接口列表就推断所有调用细节。

### 3.3 Group、Element、Fitter 的协作例子

假设：父对象挂 VerticalLayoutGroup，子对象为传统 Text，文本变长：

```text
Text.text 的非空变更分支
 → SetVerticesDirty + SetLayoutDirty
 → 向上找到有效布局根
 → 横向需求：孩子报告 preferredWidth 等
 → 横向控制：父布局给文字分配宽度
 → 纵向需求：文字按可用宽度计算 preferredHeight
 → 纵向控制：父布局分配高度和纵向位置
 → 裁剪／图形重建使用更新后的 RectTransform
```

这里特意限定非空变更：本地 [Text.text setter](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Text.cs#L200) 的置空分支仅直接调用 SetVerticesDirty，不能把“所有赋值都同时标记布局”写成无条件事实。

同对象可以有 Text 和 LayoutElement。读取布局属性时，LayoutUtility 对每个属性选择有效的最高 layoutPriority；同优先级取较大值，负值表示该组件不提供该属性。LayoutElement 因而可以覆盖文字或图片的默认尺寸需求。依据：[LayoutUtility.GetLayoutProperty](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutUtility.cs#L140)。

如果再增加 ContentSizeFitter，它根据需要的尺寸控制自己的 RectTransform。必须先明确父 Group、Fitter 各自驱动哪一个对象的哪一条轴，避免相互争抢尺寸。

### 3.4 延迟与强制布局

`MarkLayoutForRebuild` 走延迟调度；[ForceRebuildLayoutImmediate](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutRebuilder.cs#L68) 则直接对指定根执行 Layout 阶段。后者不等于完整执行所有 Canvas 渲染阶段，也不会替代图形更新和引擎绘制。

平时优先让队列合并更新。确需在当前代码段读取即时布局结果时，再基于具体依赖判断是否强制重建相应子树，不要把 ForceRebuild 当成每次改属性后的固定步骤。

## 4. 一次点击怎样到达 Button.onClick

### 4.1 入口与状态所属

[EventSystem.Update](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/EventSystem.cs#L492) 更新输入模块、判断激活／切换，并在没有切换模块且当前模块存在时调用 Process。本文以本包 StandaloneInputModule 的鼠标主线说明机制；新 Input System 模块在另一个包中实现自己的处理逻辑。

```text
EventSystem.Update
 → 当前 InputModule.Process
 → 获取／更新 PointerEventData
 → EventSystem.RaycastAll
 → 各 BaseRaycaster.Raycast
 → 汇总排序，选择有效命中
 → 输入模块判断 Press / Move / Drag / Release
 → ExecuteEvents 调用具体事件接口
```

PointerEventData 保存 position、delta、pressPosition、pointerCurrentRaycast、pointerPress、pointerClick、pointerDrag、eligibleForClick、dragging 等。**当前命中对象、按下处理对象、点击处理对象、拖拽处理对象可以不同。** 命中到按钮的 Label，并不意味着 Label 必须自己实现 Button 的行为。

### 4.2 GraphicRaycaster 如何找候选

[GraphicRaycaster.Raycast](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/GraphicRaycaster.cs#L126) 首先取得所属 Canvas 的 `GraphicRegistry.GetRaycastableGraphicsForCanvas`。本版本已经维护单独的可射线检测集合，不必把旧版“总是从所有 Graphic 中筛选”当成当前实现。

主要过程包括：显示器／相机坐标处理、可选的 2D/3D 物理阻挡、候选过滤、深度排序以及 RaycastResult 构造。内部候选过滤检查 raycastTarget、cull、depth、矩形范围等，再调用 Graphic.Raycast。

Graphic.Raycast 沿当前对象及父链查询 ICanvasRaycastFilter。CanvasGroup、Mask、RectMask2D、Image 的过滤行为可以影响结果；CanvasGroup.ignoreParentGroups 与排序覆盖 Canvas 等还影响父链遍历规则。

命中不是天然逐像素透明度检测。Image 只有相应 alphaHitTestMinimumThreshold 等条件满足时才尝试透明度过滤；不能仅凭 Image 颜色透明就推断射线穿透。

入口：[候选筛选与局部深度排序](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/GraphicRaycaster.cs#L308)、[Graphic.Raycast](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs#L842)。

EventSystem 随后对所有 Raycaster 的结果统一排序，比较相机深度、模块优先级、sortingLayer、sortingOrder、特定条件下的 depth、distance 等，不是只比较距离。depth 的跨结果比较要求相同 rootRaycaster。细节：[RaycastComparer](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/EventSystem.cs#L201)。

### 4.3 ExecuteEvents 的三种操作

| 操作 | 行为 | 常见用途 |
| --- | --- | --- |
| Execute&lt;T&gt; | 在指定 GameObject 上调用所有有效的 T 处理组件 | 对已记录的 pointerPress 发送 PointerUp |
| ExecuteHierarchy&lt;T&gt; | 从当前对象向父链寻找；遇到第一个能处理的对象，执行后停止向上 | 寻找并发送 PointerDown、Drop |
| GetEventHandler&lt;T&gt; | 向父链找到第一个处理对象并返回，不执行 | 记录点击目标、拖拽目标 |

事件系统不是所有事件都无条件向所有祖先广播，也不是完整的浏览器 DOM 捕获／冒泡模型。一个 GameObject 上可以执行多个匹配组件，和是否继续访问祖先是两个维度。

入口：[Execute](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/ExecuteEvents.cs#L248)、[ExecuteHierarchy](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/ExecuteEvents.cs#L290)、[GetEventHandler](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/ExecuteEvents.cs#L359)。

### 4.4 按下、拖动和释放

StandaloneInputModule 的鼠标逻辑可按以下状态阅读：

1. **按下**：记录按下位置与命中，设置 eligibleForClick；向上执行 PointerDown，另外查找 IPointerClickHandler，保存 pointerPress／pointerClick；查找 IDragHandler 并初始化潜在拖拽。
2. **移动**：处理 Enter／Exit。若有拖拽目标且移动超过阈值，发送 BeginDrag，再发送 Drag。
3. **拖拽中的点击资格**：PointerInputModule 在特定条件下取消按压／点击状态，例如 pointerPress 与 pointerDrag 不同。不要把“有拖拽就绝对不可能有点击”作为该版本所有路径的无条件规则。
4. **释放**：给记录的 pointerPress 发送 PointerUp；查找释放位置的点击处理器，若与记录的 pointerClick 相同且仍有点击资格，发送 PointerClick。拖拽状态还会触发 Drop、EndDrag，之后清理状态。

鼠标释放的 [ReleaseMouse](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/InputModules/StandaloneInputModule.cs#L183) 中 Click 和 Drop 是两个独立 if；触屏路径有自己的分支。逐行学习时要区分这些路径，不能从一个概括图推导所有设备的完全相同时序。

最终：

```text
ExecuteEvents.pointerClickHandler
 → Button.OnPointerClick
 → 检查是否为左键
 → Button.Press
 → 检查 IsActive / IsInteractable
 → m_OnClick.Invoke
 → 业务监听器
```

键盘／手柄的导航提交则可经当前 selected GameObject 的 ISubmitHandler → Button.OnSubmit → Press，不必经过指针命中检测。入口：[Button.OnPointerClick](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Button.cs#L109)、[OnSubmit](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Button.cs#L148)。

## 5. 状态、射线与可见性是三件事

| 设置 | 所属路径 | 不能据此推断什么 |
| --- | --- | --- |
| Selectable.interactable / CanvasGroup.interactable | 控件可交互性 | 不等于把 Graphic 从射线候选集合移除 |
| Graphic.raycastTarget | 图形命中候选 | 不控制是否绘制该图形 |
| CanvasGroup.blocksRaycasts | 父链射线过滤 | 不等于自动切换所有 Selectable 的状态 |
| CanvasGroup.alpha / Graphic.color.a | 显示透明度 | 不等于禁止命中或停止布局／重建 |
| CanvasRenderer.cull | 图形裁剪／绘制状态 | 不等于 GameObject 被禁用 |
| SetActive(false) / 禁用组件 | 生命周期及活跃性 | 与仅设 alpha=0 的行为不同 |

Selectable 通过 OnCanvasGroupChanged 维护组级交互限制，通过 DoStateTransition 将交互状态应用到 targetGraphic 或 Animator。输入命中后控件还会检查自己的业务响应条件；“被命中”和“成功触发 onClick”不是同一个判断。

## 6. 两种遮罩的实际接入点

### 6.1 Mask：材质与 stencil

```text
Mask.GetModifiedMaterial
 → 根据父级 Mask 深度设置 stencil 写入材质
 → 配置清理／恢复 stencil 的 pop material

子图形 MaskableGraphic.GetModifiedMaterial
 → 计算所处 stencil 深度
 → 生成／复用测试材质
 → Graphic.UpdateMaterial → CanvasRenderer
```

[StencilMaterial](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/StencilMaterial.cs#L52) 以基础材质与 stencil 参数等匹配缓存，使用引用计数释放，不是每次调用都必须分配永久新材质。遮罩层级变更会通过 MaskUtilities 通知相关图形重新计算。

### 6.2 RectMask2D：矩形裁剪与剔除

```text
CanvasUpdateRegistry：布局完成
 → ClipperRegistry.Cull
 → RectMask2D.PerformClipping
 → 收集有效父级裁剪矩形，计算交集
 → IClippable.SetClipRect / SetClipSoftness
 → MaskableGraphic：设置 CanvasRenderer 矩形裁剪
 → Cull：完全不可见时更新 canvasRenderer.cull
```

它不生成 stencil 写入／恢复材质，也不把每个三角形在 C# 中重新裁成多边形。矩形裁剪有空间与形状限制，应结合 Canvas、父级 RectMask2D 和图形实现判断，不能简单当作任意形状 Mask 的可互换替代品。

“渲染被裁掉”不等于“事件自动不需要过滤”。Mask 和 RectMask2D 也实现 ICanvasRaycastFilter，通过输入路径独立限制命中。

入口：[RectMask2D.PerformClipping](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/RectMask2D.cs#L204)、[MaskableGraphic.SetClipRect / Cull](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/MaskableGraphic.cs#L135)。

## 7. TMP 如何复用框架又保持独立

Canvas 文字分支是 `Graphic → MaskableGraphic → TMP_Text → TextMeshProUGUI`。TextMeshProUGUI 参与 GraphicRegistry、布局接口、遮罩协议与射线检测，同时覆盖文字更新。

本版本主要入口：

```text
TextMeshProUGUI.SetVerticesDirty
 → CanvasUpdateRegistry.RegisterCanvasElementForGraphicRebuild
 → TextMeshProUGUI.Rebuild(PreRender)
 → OnPreRenderCanvas
 → 属性／布局确实变化时：ParseInputText / 字体资源更新
 → GenerateTextMesh（自动字号可能迭代多次）
 → CanvasRenderer.SetMesh；必要时更新 TMP_SubMeshUI
```

`SetLayoutDirty` 则使 preferred 尺寸缓存失效，并调用 LayoutRebuilder.MarkLayoutForRebuild。字体 fallback、Sprite、材质变化等还可能需要子网格和附加材质，所以文字不一定对应单一绘制数据。

与此同时，`TMP_UpdateManager` 也订阅 Canvas.willRenderCanvases，维护 InternalUpdate、layout、graphic 和 culling 队列。TextMeshProUGUI.OnEnable 在相应条件下注册 InternalUpdate；3D TextMeshPro 分支也使用此管理器的文字队列。应按调用者区分用途，不能把两套管理器机械串成一条固定顺序链。

入口：[TMP SetVerticesDirty / SetLayoutDirty](../Packages/com.unity.ugui/Runtime/TMP/TextMeshProUGUI.cs#L110)、[Rebuild](../Packages/com.unity.ugui/Runtime/TMP/TextMeshProUGUI.cs#L214)、[OnPreRenderCanvas](../Packages/com.unity.ugui/Runtime/TMP/TextMeshProUGUI.cs#L2448)、[GenerateTextMesh](../Packages/com.unity.ugui/Runtime/TMP/TextMeshProUGUI.cs#L2511)、[TMP_UpdateManager](../Packages/com.unity.ugui/Runtime/TMP/TMP_UpdateManager.cs)。

## 8. 从架构理解性能，而不是先背优化结论

| 看到的现象 | 先沿哪条链排查 | 源码能支持的结论 |
| --- | --- | --- |
| 经常出现 Layout.Rebuild | setter／子节点变化 → MarkLayoutForRebuild → 布局根 | 嵌套布局或重复变更会产生查找和遍历成本 |
| 频繁生成图形 | SetVerticesDirty → Rebuild → OnPopulateMesh | 内容、尺寸或效果变化可能增加 CPU 几何成本 |
| 文字更新昂贵 | TMP 属性变化 → ParseInputText → GenerateTextMesh | 换行、自动字号、字体资源处理和多材质可能增加成本 |
| 点击穿透／被挡住 | 候选集合 → Graphic.Raycast → 排序 → 处理器 | 透明度、interactable、blocksRaycasts、raycastTarget 各管不同状态 |
| 滚动列表仍然很重 | ScrollRect.LateUpdate → 内容／布局 → 裁剪 → 重建 | RectMask2D 不等于对象复用或数据虚拟化 |
| draw call 超预期 | 提交数据 + 引擎 Profiler / Frame Debugger | 仅凭此包不能推导所有批处理细节 |

拆分 Canvas、移除装饰图形的 raycastTarget、减少无必要布局与网格变化，都应建立在实际调用链和测量上。不要从“全局队列存在”推导每帧全 Canvas 重建，也不要从“某元素进入 Graphic 队列”推导整个 Canvas 中每个 Image 都重新执行 OnPopulateMesh。

下一步可以按 [源码阅读路线](UGUI-源码阅读路线.md) 在项目中放断点验证这些链路。
