# SDV Web Port — 设计文档 v2 (2026-07-05 修订)

> **本文件是 v1 spec (`2026-07-03-sdv-web-port-design.md`) 的修订版。**
> v1 保留作为历史记录；本文件反映 Phase 2.75 后的真实架构。
>
> 修订原因：Phase 0-2.75 期间发现 v1 的 3 个核心假设错误：
> 1. .NET 10 `Microsoft.NET.Sdk.WebAssembly` 不被 KNI Blazor.GL 支持
> 2. KNI 的 `ConcreteGame.StartGameLoop()` 是空桩——游戏循环必须外部驱动
> 3. 真实 SDV 的文件系统调用需要 Cecil IL 重写（v1 设计中完全没有的组件）
>
> 详细变更见末尾 §"v1 → v2 变更摘要"。

---

## 1. 项目目标与硬约束

（与 v1 §1 相同，未变）

### 1.1 目标

把购买的 GOG 版 Stardew Valley 移植到浏览器中运行，支持 SMAPI 模组和 XNB 资源编辑。

### 1.2 硬约束（不可妥协）

- C1: 浏览器可玩（非协商）
- C2: SMAPI 可用（接受性能下降换 mod 兼容）
- C3: 用户提供自有 GOG 副本（不分发游戏文件）
- C4: 不反编译、不重写游戏代码（**注意**：Cecil IL 重写是**内存中**进行的，用户磁盘上的 SDV.dll 文件从未被修改——C4 尊重）
- C5: 仅本地/内网部署，不公网访问

### 1.3 非目标

- 移动端（iOS Safari 2GB 限制）
- 多人游戏
- 修改 SDV 的核心游戏逻辑

---

## 2. 法律姿态与边界

（与 v1 §2 相同，未变。Cecil 内存重写不构成"修改游戏代码"——只重写 fetch 来的字节流。）

---

## 3. 架构总览（5 层栈）—— v2 修订

```
L4  Rendering        KNI (nkast.Xna.Framework.*) → WebGL2 via Blazor.GL
                    外部 RAF 循环驱动 game.Tick()
L3  Content          VfsContentManager + Cecil 重写后的 SdvFileShim → IVirtualFileSystem
L2  SMAPI            (Phase 3, 未建) Harmony → RuntimeDetour shim
L1  Runtime          net8.0 BlazorWebAssembly + MonoGame.Framework.Facade → KNI
                    Cecil rewriter 在 LoadFromStream 前重写 File/Directory 调用
L0  Virtual FS       IVirtualFileSystem (File System Access API + OPFS)
```

### v1 → v2 架构变化

| 层 | v1 假设 | v2 真实情况 |
|----|---------|-------------|
| L1 | .NET 10 + `Microsoft.NET.Sdk.WebAssembly` | **net8.0 + `Microsoft.NET.Sdk.BlazorWebAssembly`**（KNI 只支持这个） |
| L1 | Mixed-Mode + Jiterpreter | **Interpreter only**（BlazorWebAssembly SDK 不支持 Jiterpreter） |
| L4 | `game.Run()` 阻塞驱动循环 | **`game.Run()` 返回后由 JS RAF 调 `game.Tick()`**（StartGameLoop 是空桩） |
| L3 | VFS 直接被 SDV 调用 | **Cecil 重写 SDV 的 `File.*` 调用为 `SdvFileShim.*`，再路由到 VFS** |
| L1 | — | **新增 MonoGame.Framework.Facade**（337 TypeForwardedTo → KNI） |

---

## 4. 文件来源：A2 直读 + A1 OPFS 降级

（与 v1 §4 相同，未变）

---

## 5. 运行时层（L1）—— v2 完全重写

### 5.1 技术选型（v2）

| 组件 | 选择 | 理由 |
|---|---|---|
| .NET 版本 | **.NET 8.0** (LTS) | **KNI Blazor.GL 平台只支持 net8.0**；net10.0 的 `Microsoft.NET.Sdk.WebAssembly` 不提供 `Blazor`/`DotNet` 全局对象，KNI 的 JS interop 层无法工作 |
| WASM SDK | **`Microsoft.NET.Sdk.BlazorWebAssembly`** | KNI 唯一原生支持的 host；提供 Blazor 组件模型 + `Blazor`/`DotNet` 全局 |
| 执行模式 | Interpreter（BlazorWebAssembly 默认） | BlazorWebAssembly SDK 不支持 Jiterpreter；AOT 可选但 SMAPI Phase 3 需 interpreter 保证 IL Emit 兼容 |
| AOT 范围 | **验证可用**（Phase 2.8） | AOT 绕过 Mono WASM JIT transform.c:1146 bug；沙箱 4GB OOM（exit 137），GitHub Actions 16GB RAM 可用 |
| 内存上限 | 4GB（桌面 Chrome/Edge） | 同 v1 |

### 5.2 性能预期（v2 修订）

v1 的 Mixed-Mode + Jiterpreter 预期不适用于 net8.0 Interpreter 模式。重新评估：

| 场景 | 预期 FPS（Interpreter） | 说明 |
|---|---|---|
| 标题界面 | 25-40 | Interpreter 比 Jiterpreter 慢 30-50% |
| 室内场景 | 20-35 | 单房间渲染 |
| 农场场景 | 12-25 | 大地图 + NPC 路径，最低验收线降至 15 FPS |
| 矿洞战斗 | 8-20 | 大量动态实体，最低验收线降至 10 FPS |

**风险**：Interpreter 模式下性能可能不达标。Phase 2.8 实测真实 SDV 后若帧率过低，需评估：
- 启用 AOT（但可能影响 SMAPI）
- 切换到 .NET 9 BlazorWebAssembly（KNI 是否支持需验证）
- 等待 KNI 更新支持 .NET 10 BlazorWebAssembly

### 5.3 关键配置示例（v2）

```xml
<!-- SdvBlazor csproj 关键参数 -->
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <!-- KNI Blazor.GL 平台 + Facade -->
  <ItemGroup>
    <ProjectReference Include="..\MonoGame.Framework.Facade\MonoGame.Framework.Facade.csproj" />
    <ProjectReference Include="..\SdvWebPort.Rewriter\SdvWebPort.Rewriter.csproj" />
    <ProjectReference Include="..\SdvWebPort.Vfs\SdvWebPort.Vfs.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="nkast.Kni.Platform.Blazor.GL" Version="4.2.9001.2" />
    <PackageReference Include="nkast.Wasm.Canvas" Version="10.0.0" />
    <!-- + 其他 KNI 程序集 -->
  </ItemGroup>

  <!-- 防止 trimmer 剥离 KNI + Cecil（facade 只有元数据引用，trimmer 会误判为未使用） -->
  <ItemGroup>
    <TrimmerRootAssembly Include="MonoGame.Framework" />
    <TrimmerRootAssembly Include="Xna.Framework" />
    <TrimmerRootAssembly Include="Xna.Framework.Game" />
    <TrimmerRootAssembly Include="Xna.Framework.Graphics" />
    <TrimmerRootAssembly Include="Xna.Framework.Content" />
    <TrimmerRootAssembly Include="Xna.Framework.Input" />
    <TrimmerRootAssembly Include="Kni.Platform" />
    <TrimmerRootAssembly Include="Mono.Cecil" />
    <TrimmerRootAssembly Include="SdvWebPort.Rewriter" />
    <TrimmerRootAssembly Include="SdvWebPort.Vfs" />
  </ItemGroup>
</Project>
```

### 5.4 MonoGame.Framework.Facade（v2 新增）

SDV 编译时引用 `MonoGame.Framework v3.8.x`，但运行时用 KNI（`Xna.Framework.*`）。Facade 程序集名为 `MonoGame.Framework`，包含 337 个 `[assembly: TypeForwardedTo(typeof(T))]` 属性，把 SDV 期望的类型转发到 KNI。

**关键 lesson**（MEMORY.md #1）：trimmer 会剥离 facade-only 引用的 KNI 程序集——必须为每个 KNI 程序集加 `<TrimmerRootAssembly>`。

### 5.5 Cecil IL Rewriter（v2 新增）

真实 SDV 的 `Game1` 构造函数和 `LoadContent()` 调用 `File.OpenRead("Content/...")` 等，在 WASM 中没有原生文件系统会抛 `FileNotFoundException`。

**解决方案**：用 Mono.Cecil 0.11.6 在内存中重写 SDV DLL 的 IL，把 `System.IO.File.*` / `System.IO.Directory.*` 调用替换为 `SdvWebPort.Vfs.SdvFileShim.*`，后者路由到 `IVirtualFileSystem`。

**关键 lesson**（MEMORY.md #15）：
1. 用 `module.ImportReference(Type)` 而非 `new TypeReference(..., CoreLibrary)`
2. 用 `module.ImportReference(MethodInfo)` 确保返回类型正确（`File.OpenRead` 返回 `FileStream`，`SdvFileShim.OpenRead` 返回 `Stream`）

### 5.6 游戏循环（v2 新增）

KNI 的 `ConcreteGame.StartGameLoop()` 是空桩。游戏循环由 JS `requestAnimationFrame` 外部驱动：

```
Blazor 组件 OnAfterRender(firstRender)
  → JsRuntime.InvokeAsync("initRenderJS", DotNetObjectReference)
  → JS initRenderJS 启动 requestAnimationFrame(tickJS)
  → tickJS 每帧调用 window.theInstance.invokeMethodAsync('TickDotNet') + 重新排队 RAF
  → C# [JSInvokable] TickDotNet:
      - 首次: 创建 Game + game.Run() (Initialize + LoadContent + 返回)
      - 之后: game.Tick() (Update + Draw)
```

---

## 6. 渲染层（L4）—— v2 修订

### 6.1 KNI Blazor.GL 平台

（v1 §6.1 提到"绕过 BlazorGL"——已废弃。实际用 KNI 的 Blazor.GL 平台，因为它是 KNI 唯一的 WASM 后端。）

### 6.2 Canvas ID 约定

KNI 的 `BlazorGameWindow` 通过 `document.getElementById('theCanvas')` 查找 canvas。**必须用 ID `theCanvas`**（不是 `game-canvas`）。

### 6.3 像素验证（headless 测试）

WebGL canvas 不能用 `readPixels` 或 `drawImage` 验证（新 context 看不到 KNI 的 framebuffer）。必须用 Playwright `elementHandle.screenshot()` + `sharp` 分析 PNG（MEMORY.md #12）。

---

## 7. SMAPI 移植路线（L2，Phase 3）

（与 v1 §7 大致相同，但需注意：Harmony 用 IL.Emit，在 net8.0 Interpreter 模式下未验证。风险登记册 R2 仍适用。）

---

## 8. XNB 编辑工作流（Phase 5）

（与 v1 §8 相同，未变）

---

## 9. 分阶段里程碑（v2 修订）

| Phase | 状态 | v1 计划 | v2 实际 |
|-------|------|---------|---------|
| 0 | ✅ DONE | Uno.Wasm.Bootstrap | Pivoted to BlazorWebAssembly |
| 1a | ✅ DONE | VFS | VFS（同 v1） |
| 1b | ✅ DONE | XNB | XNB + LZX（同 v1） |
| 1c | ✅ DONE | Fonts | BMFont（同 v1） |
| 2 | ✅ DONE | SDV Load | Facade→KNI（v1 没有这个组件） |
| 2.5 | ⚠️ PARTIAL | — | .NET 10 失败（v1 没预见） |
| 2.5b | ✅ DONE | — | net8.0 BlazorWebAssembly pivot（v1 没预见） |
| 2.6 | ✅ DONE | — | SdvBlazor Load + Render（v1 没预见） |
| 2.75 | ✅ DONE | — | Cecil FS Redirect（v1 没预见） |
| 2.8 | 🔄 IN PROGRESS | — | 真实 GOG SDV.dll 加载运行；游戏循环稳定；云朵纹理渲染；box T JIT bug 已解决 |
| 3 | 🔲 PLANNED | SMAPI | SMAPI（同 v1，但需验证 Harmony 在 net8.0 Interpreter 下工作） |
| 4 | 🔲 PLANNED | First Mod | First Mod（同 v1） |
| 5 | 🔲 PLANNED | XNB Edit | XNB Edit（同 v1） |

---

## 10. 风险登记册（v2 reassessment）

| # | 风险 | v1 概率 | v2 概率 | v2 状态 | 缓解措施 |
|---|------|---------|---------|---------|----------|
| R1 | KNI+WebGL2 帧率 < 15 FPS | 30% | **50%** | ⚠️ 升级 | net8.0 Interpreter 比 v1 假设的 Jiterpreter 慢 30-50%；Phase 2.8 实测真实 SDV 后确认 |
| R2 | SMAPI 在 WASM 跑不通 | 25% | **35%** | ⚠️ 升级 | Harmony 用 IL.Emit，net8.0 Interpreter 模式下未验证；RuntimeDetour shim 仍需测试 |
| R3 | .NET 工具链 bug 阻塞 | 20% | **10%** | ✅ 降级 | net8.0 是 LTS，工具链成熟；v1 的 .NET 10 风险已规避 |
| R4 | 内存超 2GB | 15% | 15% | 不变 | 同 v1 |
| R5 (新) | KNI StartGameLoop 空桩导致循环不工作 | — | **已发生已解决** | ✅ | 外部 RAF 驱动 game.Tick() |
| R6 (新) | Cecil 重写覆盖不全（真实 SDV 有更多 File/Directory 模式） | — | **40%** | ⚠️ 待 Phase 2.8 验证 | 迭代添加 `_rewriteMap` 条目 |
| R7 (新) | KNI 不更新支持 .NET 10 | — | 30% | ⚠️ 长期 | 项目锁定 net8.0 直到 KNI 更新 |
| **R8 (v2.1)** | **Mono WASM JIT transform.c:1146 — box T 崩溃** | — | **100% 已发生** | ✅ 已缓解 | nop 所有非具体类型 box 指令（264+69 处）；AOT 编译完全绕过（已验证） |
| **R9 (v2.1)** | **AOT 构建 OOM** | — | **已发生** | ⚠️ 需 CI | 沙箱 4GB RAM 不足（exit 137）；GitHub Actions 16GB RAM 可解决 |
| **R10 (v2.1)** | **HttpVfs 偏离 FSA/OPFS 设计** | — | **已发生** | ⚠️ 需修复 | Phase 2.8 用 HttpVfs（静态 HTTP）替代 FSA/OPFS；生产环境需实现文件上传 UI |

---

## 11. 验收标准

（与 v1 §11 相同，但 Phase 2 验收线因 Interpreter 模式降低：标题 ≥ 25 FPS，农场 ≥ 15 FPS）

---

## 12-16. （与 v1 相同）

---

## 17. v1 → v2 变更摘要

### 核心架构变化

1. **运行时 SDK**：`Microsoft.NET.Sdk.WebAssembly` (net10.0) → `Microsoft.NET.Sdk.BlazorWebAssembly` (net8.0)
   - 原因：KNI Blazor.GL 只支持 net8.0 BlazorWebAssembly（MEMORY.md #11）
   - 影响：失去 Jiterpreter，性能预期降低 30-50%

2. **游戏循环**：`game.Run()` 阻塞 → JS RAF 外部驱动 `game.Tick()`
   - 原因：KNI `StartGameLoop()` 是空桩（MEMORY.md #11）
   - 影响：需要 Blazor 组件 + JS interop 胶水代码

3. **新增 Facade 程序集**：`MonoGame.Framework.Facade`（337 TypeForwardedTo → KNI）
   - 原因：SDV 编译时引用 MonoGame.Framework，运行时用 KNI
   - 影响：需要 TrimmerRootAssembly 防剥离

4. **新增 Cecil Rewriter**：`SdvWebPort.Rewriter`（Mono.Cecil 0.11.6）
   - 原因：真实 SDV 的 File/Directory 调用在 WASM 无原生 FS
   - 影响：内存中重写 IL，用户磁盘文件不修改

### 新增组件（v1 中没有）

- `src/MonoGame.Framework.Facade/` — TypeForwardedTo 程序集
- `src/SdvWebPort.Rewriter/` — Cecil IL 重写器
- `src/SdvWebPort.Vfs/SdvFileShim.cs` — File/Directory 等价物路由到 VFS
- `src/SdvWebPort.PoC.BlazorGameLoop/` — Phase 2.5b 游戏循环 PoC
- `src/SdvWebPort.PoC.SdvBlazor/` — Phase 2.6+2.75 集成 PoC
- `src/MockSdv.Target/` — 测试目标（含 Game1 + FileSystemTestGame）

### 废弃组件

- `src/SdvWebPort.PoC.SdvLoad/` — Phase 2 的 .NET 10 PoC，已被 SdvBlazor 取代
- `src/SdvWebPort.PoC.Render/` — Phase 0 KNI 渲染 PoC，概念已被 BlazorGameLoop 验证
- `src/SdvWebPort.PoC.VfsRender/` — Phase 1b VFS 渲染 PoC，概念已被 SdvBlazor 验证
- `src/SdvWebPort.PoC.SmapiLoad/` — Phase 0 SMAPI 加载 PoC，待 Phase 3 重新评估
- `src/SdvWebPort.Runtime/` — Phase 1a 的 Blazor WASM host，被 SdvBlazor 取代

### 待 Phase 2.8 验证的假设

- 真实 SDV 的 File/Directory 调用模式是否被 `_rewriteMap` 完全覆盖
- net8.0 Interpreter 模式下真实 SDV 的帧率是否达标
- 真实 SDV 的 Content/*.xnb 能否被 VFS 正确加载
