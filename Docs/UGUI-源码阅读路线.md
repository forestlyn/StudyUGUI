# UGUI 源码阅读路线与验证实验

> 本路线围绕 UGUI 2.0.0 的架构组织，避免从大型控件逐字段阅读。先读 [整体架构](UGUI-整体架构.md)，再结合 [核心运行链路](UGUI-核心运行链路.md) 与 [交互式框架图](UGUI-架构图.html)。实验是建议步骤，本次未在 Unity 中执行。

## 1. 第一轮：把最小显示闭环走通

目标：解释“修改颜色为什么稍后才更新网格”。

1. [UIBehaviour.cs](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/UIBehaviour.cs)：只看生命周期与 UI 变化回调，认识共同基类的职责边界。
2. [Graphic.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Graphic.cs)：依次读 color、OnEnable、SetVerticesDirty、Rebuild、UpdateGeometry、DoMeshGeneration、UpdateMaterial。
3. [CanvasUpdateRegistry.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/CanvasUpdateRegistry.cs)：读构造器、两条队列、PerformUpdate、注册／去重逻辑。
4. [Image.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Image.cs#L882)：从 OnPopulateMesh 进入 Simple 分支，暂时跳过 Filled／Tiled 的几何细节。
5. [VertexHelper.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Utility/VertexHelper.cs#L207)：理解顶点／索引收集怎样变成 Mesh。

完成后应能画出：`setter → dirty flag → queue → render callback → Rebuild → mesh → CanvasRenderer`，并说出队列与 GraphicRegistry 的区别。

## 2. 第二轮：理解布局的双向依赖

| 顺序 | 文件 | 关注点 |
| --- | --- | --- |
| 1 | [ILayoutElement.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/ILayoutElement.cs) | 同文件中的 Element、Controller、Group、SelfController、Ignorer 协议 |
| 2 | [LayoutRebuilder.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutRebuilder.cs#L76) | 四步顺序、两种递归方向、有效布局根 |
| 3 | [LayoutUtility.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutUtility.cs#L140) | 一个对象上多个 Element 如何决策尺寸 |
| 4 | [LayoutGroup.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/LayoutGroup.cs) | 子节点筛选、汇总尺寸、SetChildAlongAxis、dirty 触发 |
| 5 | [HorizontalOrVerticalLayoutGroup.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/HorizontalOrVerticalLayoutGroup.cs) | 横／纵布局共用算法，以及控制尺寸与灵活尺寸 |
| 6 | [ContentSizeFitter.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/ContentSizeFitter.cs) | 控制自身尺寸，与父布局组的职责差异 |
| 7 | [CanvasScaler.cs](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Layout/CanvasScaler.cs#L237) | 渲染前缩放回调；与布局树计算分开理解 |

完成后应能回答：为什么先分配宽度再计算高度？MarkLayoutForRebuild 为何不总以 Canvas 为根？为什么 Group 同时实现 Element 和 Controller？

## 3. 第三轮：跟踪一次 Button 点击

```text
EventInterfaces
  → EventData / PointerEventData
  → EventSystem.Update / RaycastAll / RaycastComparer
  → BaseInputModule / PointerInputModule
  → StandaloneInputModule.Process / ProcessMousePress / ReleaseMouse
  → GraphicRaycaster / Graphic.Raycast
  → ExecuteEvents.Execute / ExecuteHierarchy / GetEventHandler
  → Selectable / Button
```

对应入口：[EventInterfaces](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/EventInterfaces.cs)、[PointerEventData](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/EventData/PointerEventData.cs)、[EventSystem](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/EventSystem.cs#L492)、[PointerInputModule](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/InputModules/PointerInputModule.cs#L352)、[StandaloneInputModule](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/InputModules/StandaloneInputModule.cs#L584)、[GraphicRaycaster](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/GraphicRaycaster.cs#L126)、[ExecuteEvents](../Packages/com.unity.ugui/Runtime/UGUI/EventSystem/ExecuteEvents.cs#L248)、[Selectable](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Selectable.cs#L655)、[Button](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Button.cs#L64)。

先在场景 Inspector 查看实际 InputModule。若挂载的是 InputSystemUIInputModule，继续跟踪该组件到 Input System 包，不要因为 StandaloneInputModule 断点未命中就认为 EventSystem 没有运行。本文传统模块中的状态细节不能直接作为另一输入模块每个分支的结论。

完成后应能区分：当前选中、当前悬停、当前命中、按下目标、点击目标、拖拽目标；也应能解释为什么按钮上的禁用状态不必然让鼠标射线穿透到背后的 UI。

## 4. 第四轮：接入更复杂的能力

| 模块 | 先看什么 | 然后看什么 |
| --- | --- | --- |
| 网格效果 | [IMeshModifier](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/VertexModifiers/IMeshModifier.cs) 与 BaseMeshEffect | Shadow、Outline 如何复用基础网格 |
| stencil 遮罩 | [MaskableGraphic](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/MaskableGraphic.cs#L102) | Mask、MaskUtilities、StencilMaterial |
| 矩形裁剪 | [ClipperRegistry](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Culling/ClipperRegistry.cs) | RectMask2D.PerformClipping、IClippable、Cull |
| 滚动 | [ScrollRect](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/ScrollRect.cs) 的事件接口 | Rebuild、LateUpdate、边界／惯性，再看布局与滚动条 |
| 传统文字 | [Text](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/Text.cs) 的 setter 与布局属性 | OnPopulateMesh、FontUpdateTracker |
| Canvas 上的 TMP | [TextMeshProUGUI](../Packages/com.unity.ugui/Runtime/TMP/TextMeshProUGUI.cs#L110) 的 dirty / Rebuild | TMP_Text、OnPreRenderCanvas、GenerateTextMesh、TMP_FontAsset、TMP_SubMeshUI |
| TMP 更新调度 | [TMP_UpdateManager](../Packages/com.unity.ugui/Runtime/TMP/TMP_UpdateManager.cs) | 分别查找 UGUI 文字与 3D 文字的注册调用者 |
| 编辑器创建 | [DefaultControls](../Packages/com.unity.ugui/Runtime/UGUI/UI/Core/DefaultControls.cs) | MenuOptions、InputModuleComponentFactory、各 Editor |

不要把 Image、InputField、TMP_Text 等大文件的每个分支作为第一轮目标。先跟住调用入口和输出，再根据实际问题向内部展开。

## 5. 建议的断点清单

| 想观察什么 | 断点 | 建议关注的值 |
| --- | --- | --- |
| 属性变化如何入队 | Graphic.SetVerticesDirty | this、IsActive、m_VertsDirty |
| 两类更新怎样汇合 | CanvasUpdateRegistry.PerformUpdate | layoutQueue / graphicQueue 的元素、阶段、数量 |
| 布局范围 | LayoutRebuilder.MarkLayoutForRebuild | rect、layoutRoot、父级有效 ILayoutGroup |
| 布局顺序 | LayoutRebuilder.Rebuild + PerformLayoutCalculation / Control | 根对象、回调类型、当前节点 |
| 图形生成 | Graphic.Rebuild + Image.OnPopulateMesh | update、cull、rect、顶点数 |
| 遮罩过滤 | Graphic.materialForRendering + MaskableGraphic.GetModifiedMaterial | base material、stencil depth、最终材质 |
| 射线候选 | GraphicRaycaster.Raycast | canvasGraphics、raycastTarget、cull、depth |
| 祖先事件查找 | ExecuteEvents.ExecuteHierarchy / GetEventHandler | 当前 transform、接口类型 |
| 点击资格 | 实际模块的按下／释放逻辑 | pointerPress、pointerClick、pointerDrag、eligibleForClick |
| 业务事件出口 | Button.Press | IsActive、IsInteractable、onClick 监听器 |
| TMP 文字更新 | TextMeshProUGUI.Rebuild / OnPreRenderCanvas | m_havePropertiesChanged、m_isLayoutDirty、自动字号迭代 |

Canvas.willRenderCanvases 由引擎回调进入；调试托管代码时，包内可稳定阅读的入口是 Registry 的订阅和 PerformUpdate，不必在当前包寻找原生渲染循环实现。

## 6. 可复现的学习实验

以下实验请放在单独学习场景，避免自动字号、布局组、动画等其他变量干扰最初观察。

### 实验 A：同阶段前多次改色

创建一个普通 Image，在一次业务 Update 中连续设置不同颜色，在 SetVerticesDirty 和 OnPopulateMesh 记录调用。预期多次 setter 可以产生多次 dirty 请求，但同一队列处理前对同一 Graphic 去重；最终网格使用最新状态。不要把记录写成“任何场景都每帧恰好一次”。

### 实验 B：宽度影响高度

创建 VerticalLayoutGroup 和允许换行的文字子项，为文字使用明确且一致的高度控制配置。改变父容器宽度，观察四步布局顺序与文字 preferredHeight。若没有变化，先检查布局配置是否实际控制该轴，以及文字所用组件和换行设置。

### 实验 C：比较透明、不可交互与禁止射线

在上下重叠的两个按钮上分别测试上层：alpha=0、interactable=false、Graphic.raycastTarget=false、CanvasGroup.blocksRaycasts=false。记录候选命中、按钮状态和实际 onClick，验证三条路径的差异。若有 Label 或装饰子图形，也检查它们各自的 raycastTarget。

### 实验 D：命中子节点，响应父按钮

打开 Label 的 raycastTarget，让命中结果落在文字上。观察 GetEventHandler 如何向父链找到 Button；再给子对象增加相应事件处理器，观察最近处理对象如何变化。此实验可解释子级 EventTrigger 或自定义 Handler 对父级交互的影响。

### 实验 E：点击与拖拽目标分离

在 ScrollRect 的 content 内放 Button，观察按下 Button 后开始滚动时 pointerPress／pointerClick／pointerDrag 的区别。核对实际输入模块取消点击资格的条件，不从视觉上的“拖了”直接推断某一个字段必定如何变化。

### 实验 F：对照两种遮罩

使用相同子图形分别放在 Mask 与 RectMask2D 下。跟踪 materialForRendering 和 PerformClipping：前者重点观察 stencil 材质与 pop material；后者观察矩形交集、CanvasRenderer clipping 和 cull。再用 Frame Debugger 观察引擎侧绘制，区分 C# 可见事实与最终批次。

### 实验 G：跟踪 TMP 的两条更新入口

创建 TextMeshProUGUI，修改文本，先在 SetVerticesDirty → CanvasUpdateRegistry → Rebuild 路径打断点；再在 TMP_UpdateManager.InternalUpdate 相关调用打断点，改变缩放并观察。不要预设两个渲染回调订阅者的固定先后顺序。

### 实验 H：裁剪不等于虚拟化

在 ScrollRect 内放一批固定数量的子项，通过 RectMask2D 裁掉视区外内容，比较子对象数量、布局参与、cull 状态。预期裁剪不会自动回收／复用对象；是否节省网格生成还取决于 dirty 和 cull 等条件。

## 7. 利用自带测试阅读边界

| 测试入口 | 适合学习的行为 |
| --- | --- |
| [GraphicTests.cs](../Packages/com.unity.ugui/Tests/Runtime/UGUI/Graphic/GraphicTests.cs) | 启停注册、dirty 回调、raycastTarget 的注册集合变化 |
| [ButtonTests.cs](../Packages/com.unity.ugui/Tests/Runtime/UGUI/Button/ButtonTests.cs) | 按钮事件与状态约束 |
| [Graphic/MaskTests.cs](../Packages/com.unity.ugui/Tests/Runtime/UGUI/Graphic/MaskTests.cs) | 遮罩与图形行为 |
| [RectMask2DCulling.cs](../Packages/com.unity.ugui/Tests/Editor/UGUI/RectMask2D/RectMask2DCulling.cs) | 矩形裁剪相关边界 |
| [InputModuleTests.cs](../Packages/com.unity.ugui/Tests/Editor/UGUI/EventSystem/InputModuleTests.cs) | 输入事件处理的测试组织 |
| [TMP_ControlTests.cs](../Packages/com.unity.ugui/Tests/Editor/TMP/TMP_ControlTests.cs) | TMP 控件的行为验证 |

阅读测试时先看它怎样构造 GameObject 和组件，再看一次操作后断言哪些状态。这通常比照着某段代码写一个相同实现更能帮助发现架构约束。

## 8. 最后做一次自测

能不看源码回答下列问题，说明已经建立了整体框架，而不仅仅记住类名：

- Button 背景是谁生成网格？targetGraphic 怎样连接交互与显示？
- GraphicRegistry 和 CanvasUpdateRegistry 分别保存什么？为什么一个全局调度器仍能服务多个 Canvas？
- 同一次更新里，布局、裁剪、图形重建的相对顺序是什么？
- 为什么布局要横向需求、横向控制、纵向需求、纵向控制？
- 点击 Label 时，事件怎样找到父 Button？ExecuteHierarchy 在哪里停止？
- Selected、Hover、Raycast Hit 和 Press 为什么要分别保存？
- Mask 与 RectMask2D 分别接入材质和裁剪的哪个位置？
- TextMeshProUGUI 复用了什么，又覆盖了什么？
- 哪些结论必须离开包源码，用 Unity Profiler／Frame Debugger 验证？
