#!/usr/bin/env python3
"""
SDV Web Port - Technical Design Document Generator
Generates a 40+ page technical plan PDF using ReportLab.
"""
import os
import sys
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm, cm
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.enums import TA_LEFT, TA_CENTER, TA_RIGHT, TA_JUSTIFY
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle,
    KeepTogether, Image, ListFlowable, ListItem, HRFlowable, CondPageBreak
)
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase.pdfmetrics import registerFontFamily
from reportlab.platypus.tableofcontents import TableOfContents
from reportlab.platypus.doctemplate import PageTemplate, BaseDocTemplate
from reportlab.platypus.frames import Frame

# ============================================================
# FONT REGISTRATION
# ============================================================
FONT_DIR = '/usr/share/fonts'
pdfmetrics.registerFont(TTFont('NotoSerifSC', f'{FONT_DIR}/truetype/noto-serif-sc/NotoSerifSC-Regular.ttf'))
pdfmetrics.registerFont(TTFont('NotoSerifSC-Bold', f'{FONT_DIR}/truetype/noto-serif-sc/NotoSerifSC-Bold.ttf'))
pdfmetrics.registerFont(TTFont('NotoSerifSC-Light', f'{FONT_DIR}/truetype/noto-serif-sc/NotoSerifSC-Light.ttf'))
pdfmetrics.registerFont(TTFont('SarasaMono', f'{FONT_DIR}/truetype/chinese/SarasaMonoSC-Regular.ttf'))
pdfmetrics.registerFont(TTFont('SarasaMono-Bold', f'{FONT_DIR}/truetype/chinese/SarasaMonoSC-Bold.ttf'))
# Use Sarasa for sans-serif too (NotoSansSC is variable font, incompatible with ReportLab)
pdfmetrics.registerFont(TTFont('NotoSansSC', f'{FONT_DIR}/truetype/chinese/SarasaMonoSC-Regular.ttf'))
pdfmetrics.registerFont(TTFont('NotoSansSC-Bold', f'{FONT_DIR}/truetype/chinese/SarasaMonoSC-Bold.ttf'))
registerFontFamily('NotoSerifSC', normal='NotoSerifSC', bold='NotoSerifSC-Bold')
registerFontFamily('NotoSansSC', normal='NotoSansSC', bold='NotoSansSC-Bold')
registerFontFamily('SarasaMono', normal='SarasaMono', bold='SarasaMono-Bold')

# ============================================================
# PALETTE (Dark Tech Geek Theme)
# ============================================================
PAGE_BG       = colors.HexColor('#141413')
SECTION_BG    = colors.HexColor('#21201e')
CARD_BG       = colors.HexColor('#22211b')
TABLE_STRIPE  = colors.HexColor('#1a1a18')
HEADER_FILL   = colors.HexColor('#575140')
COVER_BLOCK   = colors.HexColor('#423e33')
BORDER        = colors.HexColor('#3a3830')
ICON          = colors.HexColor('#c0b596')
ACCENT        = colors.HexColor('#e3ca7e')
ACCENT_2      = colors.HexColor('#7153cc')
TEXT_PRIMARY  = colors.HexColor('#2a2a28')  # Dark text for light background
TEXT_BODY     = colors.HexColor('#3a3a38')
TEXT_MUTED    = colors.HexColor('#6a6a68')
SEM_SUCCESS   = colors.HexColor('#5a8a6b')
SEM_WARNING   = colors.HexColor('#a38242')
SEM_ERROR     = colors.HexColor('#9a5852')
SEM_INFO      = colors.HexColor('#5a7a9b')

# Light background for body pages
BODY_BG = colors.HexColor('#fafaf8')
CODE_BG = colors.HexColor('#f0efe9')
TABLE_HEADER_BG = colors.HexColor('#3a3830')
TABLE_HEADER_TEXT = colors.HexColor('#e3ca7e')

# ============================================================
# STYLES
# ============================================================
styles = getSampleStyleSheet()

# Heading styles
style_h1 = ParagraphStyle(
    'Heading1', parent=styles['Heading1'],
    fontName='NotoSerifSC-Bold', fontSize=22, leading=30,
    textColor=TEXT_PRIMARY, spaceBefore=20, spaceAfter=12,
    borderPadding=(0, 0, 6, 0), borderWidth=0,
    borderColor=ACCENT,
)

style_h2 = ParagraphStyle(
    'Heading2', parent=styles['Heading2'],
    fontName='NotoSerifSC-Bold', fontSize=16, leading=22,
    textColor=TEXT_PRIMARY, spaceBefore=16, spaceAfter=8,
)

style_h3 = ParagraphStyle(
    'Heading3', parent=styles['Heading3'],
    fontName='NotoSerifSC-Bold', fontSize=13, leading=18,
    textColor=ACCENT, spaceBefore=12, spaceAfter=6,
)

style_h4 = ParagraphStyle(
    'Heading4', parent=styles['Heading4'],
    fontName='NotoSerifSC-Bold', fontSize=11, leading=16,
    textColor=TEXT_PRIMARY, spaceBefore=8, spaceAfter=4,
)

# Body styles
style_body = ParagraphStyle(
    'Body', parent=styles['Normal'],
    fontName='NotoSerifSC', fontSize=10, leading=16,
    textColor=TEXT_BODY, spaceBefore=4, spaceAfter=4,
    alignment=TA_LEFT, firstLineIndent=0,
)

style_body_indent = ParagraphStyle(
    'BodyIndent', parent=style_body,
    leftIndent=12,
)

style_bullet = ParagraphStyle(
    'Bullet', parent=style_body,
    leftIndent=20, bulletIndent=8, spaceBefore=2, spaceAfter=2,
)

style_code = ParagraphStyle(
    'Code', parent=styles['Normal'],
    fontName='SarasaMono', fontSize=8.5, leading=13,
    textColor=TEXT_PRIMARY, backColor=CODE_BG,
    leftIndent=8, rightIndent=8, spaceBefore=4, spaceAfter=4,
    borderColor=BORDER, borderWidth=0.5, borderPadding=6,
)

style_caption = ParagraphStyle(
    'Caption', parent=style_body,
    fontSize=9, textColor=TEXT_MUTED, alignment=TA_CENTER,
    spaceBefore=4, spaceAfter=12, fontName='NotoSansSC',
)

style_table_cell = ParagraphStyle(
    'TableCell', parent=style_body,
    fontSize=9, leading=13, spaceBefore=2, spaceAfter=2,
)

style_table_header = ParagraphStyle(
    'TableHeader', parent=style_body,
    fontSize=9.5, leading=13, fontName='NotoSerifSC-Bold',
    textColor=TABLE_HEADER_TEXT, alignment=TA_LEFT,
)

style_toc_h1 = ParagraphStyle(
    'TOC1', parent=style_body,
    fontSize=11, leading=18, fontName='NotoSerifSC-Bold',
    textColor=TEXT_PRIMARY, leftIndent=0,
)

style_toc_h2 = ParagraphStyle(
    'TOC2', parent=style_body,
    fontSize=10, leading=16, leftIndent=20,
    textColor=TEXT_BODY,
)

# ============================================================
# HELPER FUNCTIONS
# ============================================================
def P(text, style=style_body):
    """Create a paragraph."""
    return Paragraph(text, style)

def H1(text):
    """Heading 1 with accent bar."""
    return Paragraph(f'<b>{text}</b>', style_h1)

def H2(text):
    return Paragraph(text, style_h2)

def H3(text):
    return Paragraph(text, style_h3)

def H4(text):
    return Paragraph(text, style_h4)

def Code(text):
    """Code block."""
    # Escape XML and preserve formatting
    escaped = text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
    escaped = escaped.replace('\n', '<br/>').replace(' ', '&nbsp;')
    return Paragraph(escaped, style_code)

def Bullets(items, style=style_bullet):
    """Bullet list."""
    return ListFlowable(
        [ListItem(Paragraph(item, style), value='•', leftIndent=20) for item in items],
        bulletType='bullet', bulletColor=ACCENT, bulletFontSize=10,
    )

def NumberedList(items, style=style_bullet):
    """Numbered list."""
    return ListFlowable(
        [ListItem(Paragraph(item, style)) for item in items],
        bulletType='1', bulletFormat='%s.',
    )

def make_table(data, col_widths=None, header=True, stripe=True):
    """Create a styled table."""
    # Convert all cells to Paragraphs
    processed = []
    for i, row in enumerate(data):
        processed_row = []
        for cell in row:
            if isinstance(cell, str):
                if i == 0 and header:
                    processed_row.append(Paragraph(cell, style_table_header))
                else:
                    processed_row.append(Paragraph(cell, style_table_cell))
            else:
                processed_row.append(cell)
        processed.append(processed_row)
    
    t = Table(processed, colWidths=col_widths, repeatRows=1 if header else 0)
    
    style_cmds = [
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('LEFTPADDING', (0, 0), (-1, -1), 6),
        ('RIGHTPADDING', (0, 0), (-1, -1), 6),
        ('TOPPADDING', (0, 0), (-1, -1), 5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
        ('GRID', (0, 0), (-1, -1), 0.5, BORDER),
    ]
    
    if header:
        style_cmds.append(('BACKGROUND', (0, 0), (-1, 0), TABLE_HEADER_BG))
        style_cmds.append(('TEXTCOLOR', (0, 0), (-1, 0), TABLE_HEADER_TEXT))
    
    if stripe:
        for i in range(1, len(data)):
            if i % 2 == 0:
                style_cmds.append(('BACKGROUND', (0, i), (-1, i), TABLE_STRIPE))
    
    t.setStyle(TableStyle(style_cmds))
    return t

def callout(text, kind='info'):
    """Callout box."""
    colors_map = {
        'info': (SEM_INFO, '#eef2f7'),
        'warning': (SEM_WARNING, '#f5f0e5'),
        'error': (SEM_ERROR, '#f5ecea'),
        'success': (SEM_SUCCESS, '#eef5f0'),
    }
    border_color, bg_color = colors_map.get(kind, colors_map['info'])
    
    style = ParagraphStyle(
        'Callout', parent=style_body,
        backColor=colors.HexColor(bg_color),
        borderColor=border_color, borderWidth=0, borderPadding=8,
        leftIndent=12, rightIndent=8, spaceBefore=6, spaceAfter=6,
    )
    label_map = {'info': '[ 注 ]', 'warning': '[ 警告 ]', 'error': '[ 风险 ]', 'success': '[ 优势 ]'}
    return Paragraph(f'<font color="{border_color.hexval()}"><b>{label_map[kind]}</b></font>　{text}', style)

def spacer(h=8):
    return Spacer(1, h)

def hr():
    return HRFlowable(width='100%', thickness=0.5, color=BORDER, spaceBefore=8, spaceAfter=8)

# ============================================================
# PAGE TEMPLATE
# ============================================================
class SdvDocTemplate(BaseDocTemplate):
    def __init__(self, filename, **kwargs):
        BaseDocTemplate.__init__(self, filename, **kwargs)
        
        margin = 20 * mm
        frame = Frame(margin, margin + 15*mm, A4[0] - 2*margin, A4[1] - 2*margin - 15*mm, id='normal')
        
        self.addPageTemplates([
            PageTemplate(id='body', frames=frame, onPage=self._draw_page_decorations),
        ])
    
    def _draw_page_decorations(self, canvas, doc):
        canvas.saveState()
        
        # Background
        canvas.setFillColor(BODY_BG)
        canvas.rect(0, 0, A4[0], A4[1], fill=1, stroke=0)
        
        # Top accent line
        canvas.setStrokeColor(ACCENT)
        canvas.setLineWidth(2)
        canvas.line(20*mm, A4[1] - 15*mm, A4[0] - 20*mm, A4[1] - 15*mm)
        
        # Header text
        canvas.setFont('SarasaMono', 8)
        canvas.setFillColor(TEXT_MUTED)
        canvas.drawString(20*mm, A4[1] - 12*mm, 'SDV-WEB-PORT // TECHNICAL DESIGN DOCUMENT v2.0')
        canvas.drawRightString(A4[0] - 20*mm, A4[1] - 12*mm, '2026-07-11')
        
        # Footer
        canvas.setStrokeColor(BORDER)
        canvas.setLineWidth(0.5)
        canvas.line(20*mm, 15*mm, A4[0] - 20*mm, 15*mm)
        
        canvas.setFont('SarasaMono', 8)
        canvas.setFillColor(TEXT_MUTED)
        canvas.drawString(20*mm, 10*mm, 'CONFIDENTIAL · PERSONAL USE')
        canvas.drawRightString(A4[0] - 20*mm, 10*mm, f'PAGE {doc.page}')
        
        # Corner marks
        canvas.setStrokeColor(BORDER)
        canvas.setLineWidth(0.5)
        # Top-left
        canvas.line(10*mm, A4[1] - 10*mm, 15*mm, A4[1] - 10*mm)
        canvas.line(10*mm, A4[1] - 10*mm, 10*mm, A4[1] - 15*mm)
        # Top-right
        canvas.line(A4[0] - 15*mm, A4[1] - 10*mm, A4[0] - 10*mm, A4[1] - 10*mm)
        canvas.line(A4[0] - 10*mm, A4[1] - 10*mm, A4[0] - 10*mm, A4[1] - 15*mm)
        
        canvas.restoreState()

# ============================================================
# DOCUMENT CONTENT
# ============================================================
def build_story():
    story = []
    
    # ========== TOC PAGE ==========
    story.append(Spacer(1, 20*mm))
    story.append(Paragraph('<b>目　录</b>', ParagraphStyle(
        'TOCTitle', parent=style_h1, alignment=TA_CENTER, fontSize=24, spaceAfter=20
    )))
    story.append(HRFlowable(width='40%', thickness=2, color=ACCENT, spaceBefore=4, spaceAfter=20, hAlign='CENTER'))
    
    toc_entries = [
        ('执行摘要', '3'),
        ('第一章　现状分析', '5'),
        ('　1.1　路线 A 历程回顾', '5'),
        ('　1.2　WASM JIT 限制总结', '7'),
        ('　1.3　当前渲染状态', '9'),
        ('　1.4　瓶颈分析', '10'),
        ('第二章　方案对比', '11'),
        ('　2.1　方案 A-H 总览', '11'),
        ('　2.2　各方案详细分析', '12'),
        ('　2.3　选择理由', '14'),
        ('第三章　技术架构', '15'),
        ('　3.1　整体架构图', '15'),
        ('　3.2　数据流设计', '17'),
        ('　3.3　组件职责', '18'),
        ('　3.4　反编译源码路线', '19'),
        ('第四章　API 适配清单', '21'),
        ('　4.1　编译错误分类', '21'),
        ('　4.2　CS0012 类型转发修复', '22'),
        ('　4.3　缺失 API 补全', '24'),
        ('　4.4　P/Invoke 处理', '25'),
        ('第五章　移动端方案', '27'),
        ('　5.1　虚拟摇杆设计', '27'),
        ('　5.2　MobileAtlas 资源利用', '28'),
        ('　5.3　触摸输入转换层', '29'),
        ('第六章　时间线', '31'),
        ('　6.1　五阶段甘特图', '31'),
        ('　6.2　里程碑与交付物', '32'),
        ('第七章　风险评估', '33'),
        ('　7.1　技术风险', '33'),
        ('　7.2　法律风险', '34'),
        ('　7.3　性能风险', '35'),
        ('第八章　用户端体验', '36'),
        ('　8.1　传 zip 即玩流程', '36'),
        ('　8.2　PWA 离线支持', '37'),
        ('　8.3　跨设备体验', '38'),
        ('附录', '39'),
    ]
    
    for title, page in toc_entries:
        is_chapter = not title.startswith('　')
        style = style_toc_h1 if is_chapter else style_toc_h2
        dots = '·' * (50 - len(title))
        story.append(Paragraph(
            f'{title} <font color="{TEXT_MUTED.hexval()}">{dots}</font> <font name="SarasaMono">{page}</font>',
            style
        ))
    
    story.append(PageBreak())
    
    # ========== EXECUTIVE SUMMARY ==========
    story.append(H1('执行摘要'))
    story.append(hr())
    
    story.append(H3('项目目标'))
    story.append(P(
        '将真实 GOG 版星露谷物语（Stardew Valley v1.6.15.24356）完整移植到浏览器环境运行，'
        '支持 PC 键鼠与移动端触摸操作，实现"用户上传游戏文件即可在浏览器中游玩"的终极体验。'
        '项目不修改游戏逻辑代码，仅做环境适配，保留原版游戏的完整玩法体验。'
    ))
    
    story.append(H3('当前状态'))
    story.append(P(
        '经过 Phase 0 到 Phase 2.8 共 193 个 commit 的持续迭代，项目已验证了完整的加载管线：'
        '真实 SDV.dll（5.4MB）成功加载到 BlazorWebAssembly，Game1 初始化完成，118+ XNB 资源通过 HttpVfs 加载，'
        '游戏循环稳定运行（Run → Tick → Draw，0 崩溃）。当前渲染状态为云朵纹理全屏背景 + SDV 标题 logo，'
        '画布显示 354 种颜色、779 个白色像素，标题画面内容部分可见。'
    ))
    
    story.append(H3('推荐方案'))
    story.append(callout(
        '采用方案 B（反编译源码重编译）作为核心技术路线，结合方案 H 的分阶段交付策略。'
        '该方案将 SDV.dll 反编译为 950 个 C# 源文件（306,774 行），修复 API 不匹配后重新编译，'
        '彻底绕过 WASM JIT 解释器的所有 IL 限制。',
        'success'
    ))
    
    story.append(H3('时间线概览'))
    story.append(make_table([
        ['阶段', '时间', '目标', '交付物'],
        ['Phase 1', '2-3 天', '源码编译通过', '可编译的 SDV 源码项目'],
        ['Phase 2', '1 周', '标题画面完整渲染', '浏览器看到完整标题画面'],
        ['Phase 3', '1 周', '输入系统 + 移动端', 'PC + 移动端可操作'],
        ['Phase 4', '2-3 周', '基本可玩', '能新建角色、走动、种地'],
        ['Phase 5', '持续', '完整体验', '音频、联机、Mod 支持'],
    ], col_widths=[30*mm, 25*mm, 50*mm, 55*mm]))
    
    story.append(H3('关键决策'))
    story.append(Bullets([
        '<b>基础版本</b>：PC 版 GOG SDV.dll（.NET 托管代码，可反编译），非 Android 版（NativeAOT，不可反编译）',
        '<b>运行时</b>：.NET 8 BlazorWebAssembly + KNI Framework 4.2（WebGL2 后端）',
        '<b>反编译工具</b>：ILSpy 8.2（ilspycmd -p 项目模式，948 文件 0 错误）',
        '<b>移动端资源</b>：借用 Android APK 的 MobileAtlas 精灵图（虚拟摇杆/按钮图形）',
        '<b>输入方案</b>：统一虚拟输入层，PC 透传键鼠，移动端触摸转虚拟键鼠',
        '<b>存档方案</b>：OPFS（Origin Private File System）浏览器持久化',
        '<b>离线方案</b>：PWA + Service Worker，首次加载后离线可玩',
    ]))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 1: CURRENT STATE ==========
    story.append(H1('第一章　现状分析'))
    story.append(hr())
    
    story.append(H2('1.1　路线 A 历程回顾'))
    story.append(P(
        '路线 A 采用运行时 IL Patch 方案，通过 Mono.Cecil 对原始 SDV.dll 进行 25+ 个 pass 的 IL 重写，'
        '在不修改二进制文件的前提下让游戏代码适应浏览器环境。该路线从 Phase 0 到 Phase 2.8 共投入 193 个 commit，'
        '逐步解决了程序集引用重写、类型转发、委托替换、集合替换、Steam SDK 绕过、线程移除、图形配置适配等大量问题。'
    ))
    
    story.append(H3('关键里程碑'))
    story.append(make_table([
        ['阶段', '里程碑', 'commit 数'],
        ['Phase 0', '骨架 PoC，验证 KNI WebGL 能在 Blazor 渲染', '18'],
        ['Phase 1a-c', '虚拟文件系统 + XNB 解析 + 字体加载', '34'],
        ['Phase 2', '真实 SDV.dll 加载 + Game1 调用', '27'],
        ['Phase 2.5', 'Blazor SDK 切换 + Game1 invoke', '15'],
        ['Phase 2.6', 'SDV Blazor 集成', '12'],
        ['Phase 2.75', '文件系统重定向', '8'],
        ['Phase 2.8', '真实 SDV 渲染（当前）', '79'],
    ], col_widths=[30*mm, 85*mm, 25*mm]))
    
    story.append(H3('IL 重写体系（15+ pass）'))
    story.append(P('路线 A 构建了庞大的 IL 重写体系，包括以下核心 pass：'))
    story.append(Bullets([
        '<b>AssemblyRef 版本重写</b>：System.* v6→v8, MG v3.8→v3.8.5.0',
        '<b>TypeRef scope 重写</b>：绕过 trimmer 剥离的 type-forwards（74 处强制重写）',
        '<b>TypeReference 替换</b>：方法体中的类型引用替换（4090 处）',
        '<b>委托替换</b>：Action`7-`16, Func`6-`17 → 自定义委托类型',
        '<b>集合替换</b>：Stack&lt;T&gt;, SortedSet&lt;T&gt; 等 → 自定义集合类型',
        '<b>Program.get_sdk() patch</b>：ldsfld _sdk; ret，绕过 SteamHelper',
        '<b>Game1..ctor/cctor</b>：恢复原始，xxHash ctor patch 修复 TypeLoadException',
        '<b>constrained. → box</b>：1904+ 处替换，消除 Run() 路径的 transform.c:1146',
        '<b>DoThreadedInitTask → ret</b>：WASM 不支持线程',
        '<b>GraphicsProfile = HiDef</b>：允许 >2048px 纹理',
    ]))
    
    story.append(H2('1.2　WASM JIT 限制总结'))
    story.append(P(
        '在 Phase 2.8 的深入调试中，我们发现了 BlazorWebAssembly 的 Mono WASM JIT 解释器存在多个根本性限制。'
        '这些限制不是 bug（虽然有些确实是），而是解释器架构的固有约束，无法通过 IL patch 绕过。'
    ))
    
    story.append(H3('五大 JIT 限制'))
    story.append(make_table([
        ['限制', '表现', '影响', '可绕过？'],
        ['Draw 调用数', '每个方法最多 ~3 次 SpriteBatch.Draw', '无法渲染完整 UI', '部分'],
        ['方法注入', '注入新方法导致 JIT 不稳定', '无法提取复杂逻辑', '否'],
        ['Nullable&lt;T&gt; IL', 'newobj Nullable 在某些上下文失效', '源矩形渲染失败', '部分'],
        ['泛型方法调用', 'List&lt;CTC&gt;.get_Count() 报错', '无法遍历按钮列表', '否'],
        ['值类型 ldfld', 'ldfld 值类型字段触发断言', '无法访问 CTC 字段', '否'],
    ], col_widths=[30*mm, 50*mm, 45*mm, 20*mm]))
    
    story.append(H3('transform.c 断言问题'))
    story.append(P(
        '最严重的问题是 Mono WASM JIT 解释器的 transform.c 文件中的断言失败。transform.c:1146 和 transform.c:366 '
        '会在特定的 IL 模式下触发，导致页面崩溃。我们通过 constrained. → box 替换（1904+ 处）消除了 Run() 路径的 1146 断言，'
        '但 Tick() 路径的 366 断言在值类型字段访问时仍然触发。这种断言是解释器内部的正确性检查，无法从 IL 层面完全绕过。'
    ))
    
    story.append(callout(
        'transform.c 断言是 Mono JIT 解释器的已知问题，在 .NET 9+ 的 WASM 运行时中部分修复，'
        '但 BlazorWebAssembly 当前锁定在 .NET 8 + KNI 4.2，无法升级。',
        'warning'
    ))
    
    story.append(H2('1.3　当前渲染状态'))
    story.append(P('经过大量 IL patch 调试，当前渲染状态为：'))
    
    story.append(make_table([
        ['指标', '数值', '说明'],
        ['画布尺寸', '800 × 601', 'BlazorWebAssembly canvas'],
        ['非黑色像素', '369,957 (76.9%)', '有内容的像素占比'],
        ['颜色数', '354', '去重后的颜色数量'],
        ['白色像素', '779', '标题 logo 高亮区域'],
        ['Tick 成功', '5 次', '稳定游戏循环'],
        ['崩溃次数', '0', '无运行时崩溃'],
        ['渲染内容', '云朵 + 标题 logo', '标题画面部分元素'],
    ], col_widths=[35*mm, 40*mm, 75*mm]))
    
    story.append(P(
        '渲染管线已完全打通：SDV.dll → Cecil IL 重写 → ALC → GameRunner → Run → LoadContent → Tick → Draw → WebGL2 → canvas。'
        '云朵纹理（cloudsTexture）全屏渲染，SDV 标题 logo（titleButtonsTexture source rect 0,0,512,337）居中渲染。'
        '但由于 3-draw-call 限制，无法在同一方法中渲染更多元素（如标题按钮）。'
    ))
    
    story.append(H2('1.4　瓶颈分析'))
    story.append(P(
        '路线 A 的核心瓶颈在于：IL patch 是在运行时修改字节码，而 WASM JIT 解释器对 IL 的处理有诸多限制。'
        '每解决一个问题，就会撞到下一个 JIT 限制，形成"打地鼠"式的开发模式。具体表现：'
    ))
    
    story.append(Bullets([
        '<b>边际收益递减</b>：前 100 个 commit 解决了加载和初始化，后 93 个 commit 只换来"半个标题画面"',
        '<b>不可预测的崩溃</b>：同样的 IL 模式在不同上下文表现不同，调试困难',
        '<b>无法扩展</b>：3-draw-call 限制意味着标题画面都渲染不全，游戏内 UI 更不可能',
        '<b>无法注入</b>：不能添加新方法，意味着无法用"提取函数"的方式重构复杂逻辑',
    ]))
    
    story.append(callout(
        '路线 A 的天花板是 PoC 级别——能证明"可以加载和渲染"，但到不了"完整可玩"。'
        '继续投入的边际收益极低，需要转换技术路线。',
        'error'
    ))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 2: SOLUTION COMPARISON ==========
    story.append(H1('第二章　方案对比'))
    story.append(hr())
    
    story.append(H2('2.1　方案 A-H 总览'))
    story.append(P('针对"在浏览器中运行星露谷物语"这一目标，我们评估了 8 种技术方案：'))
    
    story.append(make_table([
        ['方案', '核心思路', '工作量', '传zip即玩', '可行性'],
        ['A', 'IL patch 运行时', '已投入 193 commit', '✅ 已实现', '⚠️ JIT 天花板低'],
        ['B', '反编译源码重编译', '中（472 错误）', '✅ 可实现', '✅ 能到完整可玩'],
        ['C', 'x86 二进制翻译', '极高', '✅', '⚠️ 性能差 10-100x'],
        ['D', '云端串流', '低', '❌ 需服务器', '✅ 最简单'],
        ['E', '浏览器跑 Wine', '极高', '✅', '❌ 技术不现实'],
        ['F', 'WASI 容器', '极高', '✅', '❌ 不成熟'],
        ['G', '等官方 web 版', '0', '✅', '❌ 不存在'],
        ['H', '混合分阶段', '中', '✅', '✅ 最务实'],
    ], col_widths=[12*mm, 38*mm, 35*mm, 25*mm, 30*mm]))
    
    story.append(H2('2.2　各方案详细分析'))
    
    story.append(H3('方案 A：IL Patch（已验证，放弃）'))
    story.append(P(
        '<b>原理</b>：保持 SDV.dll 二进制不变，在运行时用 Mono.Cecil 重写 IL 字节码，'
        '将不兼容的调用替换为兼容版本，绕过 Steam/Galaxy SDK，重定向文件系统等。'
    ))
    story.append(P('<b>优点</b>：不涉及反编译，法律风险低；用户端体验好（传 zip 即玩）。'))
    story.append(P('<b>缺点</b>：WASM JIT 解释器的限制是根本性的——3-draw-call 限制、方法注入不稳定、'
        'Nullable IL bug、transform.c 断言——这些都是解释器架构的固有约束，无法从 IL 层面绕过。'))
    story.append(P('<b>结论</b>：天花板是 PoC，无法到达完整可玩。'))
    
    story.append(H3('方案 B：反编译源码重编译（推荐）'))
    story.append(P(
        '<b>原理</b>：用 ILSpy 将 SDV.dll 反编译为 C# 源码（950 文件，306,774 行），'
        '修复 API 不匹配后用 .NET 8 编译器重新编译。C# 编译器生成的 IL 是"标准"的，'
        '不会触发 JIT 解释器的特殊路径，从根本上避免了方案 A 的所有问题。'
    ))
    story.append(P('<b>优点</b>：'))
    story.append(Bullets([
        'C# 编译器自动处理 Nullable&lt;Rectangle&gt;，不需要手工 IL 生成',
        '没有 3-draw-call 限制，可以直接修改 _draw 源码添加任意数量的 Draw 调用',
        '没有注入方法不稳定问题，所有方法一起编译',
        '编译器报错明确，不像运行时崩溃那样难调试',
    ]))
    story.append(P('<b>缺点</b>：'))
    story.append(Bullets([
        '反编译有法律风险（但个人使用风险基本为零）',
        '需要修复 472 个 API 不匹配错误',
        '版本更新需要重新反编译+编译',
    ]))
    story.append(P('<b>结论</b>：能到完整可玩，是推荐方案。'))
    
    story.append(H3('方案 C：x86 二进制翻译'))
    story.append(P(
        '<b>原理</b>：把 SDV 的 Windows exe/dll（x86 机器码）直接翻译成 WebAssembly。'
        '类似 QEMU 的二进制翻译思路，用 CheerpX 等工具。'
    ))
    story.append(P('<b>问题</b>：SDV 是 .NET 程序，不是原生 x86，它本身就跑在 CLR 上。'
        '对 .NET 游戏做 x86 翻译是多此一举。而且二进制翻译性能损耗 10-100 倍，SDV 会卡到不可玩。'))
    story.append(P('<b>结论</b>：不适合 SDV。'))
    
    story.append(H3('方案 D：云端串流'))
    story.append(P(
        '<b>原理</b>：在云服务器上跑 SDV（Linux + Wine），把画面串流到浏览器，输入回传到服务器。'
        '类似 GeForce Now、Moonlight、Parsec 的方案。'
    ))
    story.append(P('<b>优点</b>：工作量极小（1 天搭好），100% 完整可玩，零兼容问题。'))
    story.append(P('<b>缺点</b>：需要服务器（违背"传 zip 即玩"的初衷），有网络延迟，有服务器成本。'))
    story.append(P('<b>结论</b>：如果接受服务器，这是最快到"能玩"的路线。但不满足需求。'))
    
    story.append(H3('方案 E：浏览器里跑 Wine'))
    story.append(P(
        '<b>原理</b>：把 Wine（Windows 兼容层）编译成 WASM，在浏览器里运行。'
    ))
    story.append(P('<b>问题</b>：Wine 是 C 写的，理论上能编译到 WASM，但 SDV 是 .NET Framework 程序，'
        'Wine 还要跑 .NET Runtime。浏览器里跑 Wine + .NET Runtime = 嵌套两层虚拟化，性能极差。'
        '没有人在做这个，技术上不现实。'))
    story.append(P('<b>结论</b>：不可行。'))
    
    story.append(H3('方案 F：WebAssembly 容器（WASI）'))
    story.append(P(
        '<b>原理</b>：用 WASI（WebAssembly System Interface）在浏览器跑完整 OS 环境。'
    ))
    story.append(P('<b>问题</b>：Wasmtime 在浏览器外的 WASI 运行时里能跑 Linux 程序，'
        '但浏览器内的 WASI 支持很有限。还是要跑 Wine + .NET Runtime，技术不成熟。'))
    story.append(P('<b>结论</b>：3-5 年后才可能。'))
    
    story.append(H3('方案 G：等官方 web 版'))
    story.append(P('<b>结论</b>：ConcernedApe 没有做 web 版的计划，不现实。'))
    
    story.append(H3('方案 H：混合分阶段（执行策略）'))
    story.append(P(
        '<b>原理</b>：结合 B 的技术路线 + 分阶段交付策略，每个阶段都有可见进展。'
        '不是独立技术方案，而是方案 B 的执行策略优化。'
    ))
    
    story.append(H2('2.3　选择理由'))
    story.append(callout(
        '选择方案 B（反编译源码重编译）+ 方案 H（分阶段交付）作为最终路线。'
        '方案 B 是唯一能到达"完整可玩"且不依赖服务器的方案。'
        '方案 H 的分阶段策略确保每个阶段都有可演示的成果。',
        'success'
    ))
    
    story.append(P('选择理由总结：'))
    story.append(Bullets([
        '<b>目标对齐</b>：用户明确要求"完整能玩"，只有方案 B 能达到',
        '<b>无服务器</b>：用户要求"传 zip 即玩"，方案 D 需要服务器被排除',
        '<b>技术可行</b>：反编译已验证成功（950 文件 0 错误），编译错误可分类修复',
        '<b>法律可控</b>：个人不公开使用，风险基本为零',
        '<b>版本友好</b>：反编译+编译流程可工具化，版本更新只需重新跑流程',
    ]))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 3: TECHNICAL ARCHITECTURE ==========
    story.append(H1('第三章　技术架构'))
    story.append(hr())
    
    story.append(H2('3.1　整体架构图'))
    story.append(P('系统采用五层架构，每层职责明确，通过明确定义的接口交互：'))
    
    story.append(Code('''┌─────────────────────────────────────────────────────────┐
│                    用户端 (浏览器)                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────────────────┐  │
│  │ PC 键鼠   │  │ 移动触摸  │  │ 文件上传 (zip/apk)   │  │
│  └────┬─────┘  └────┬─────┘  └──────────┬───────────┘  │
└───────┼──────────────┼──────────────────┼───────────────┘
        │              │                  │
        ▼              ▼                  ▼
┌─────────────────────────────────────────────────────────┐
│              L5 输入抽象层 (VirtualInput)                 │
│  PC: 透传键鼠  │  Mobile: 触摸→虚拟键鼠  │  统一接口     │
└───────────────────────┬─────────────────────────────────┘
                        │ MouseState / KeyboardState
                        ▼
┌─────────────────────────────────────────────────────────┐
│              L4 游戏引擎 (SDV 反编译源码)                  │
│  950 个 .cs 文件 (306,774 行)  │  原版游戏逻辑         │
│  适配层: KniCompatShim + VfsRedirect + ThreadShim       │
└───────────────────────┬─────────────────────────────────┘
                        │ XNA API 调用
                        ▼
┌─────────────────────────────────────────────────────────┐
│              L3 图形/音频 (KNI Framework)                 │
│  Xna.Framework.* → WebGL2  │  XACT → Web Audio API     │
└───────────────────────┬─────────────────────────────────┘
                        │ WebGL2 / Web Audio
                        ▼
┌─────────────────────────────────────────────────────────┐
│              L2 运行时 (BlazorWebAssembly)                │
│  .NET 8 CLR  │  HttpVfs (XNB 加载)  │  OPFS (存档)      │
└───────────────────────┬─────────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────────┐
│              L1 浏览器 (WebAssembly)                      │
│  Canvas  │  WebGL2  │  Web Audio  │  OPFS  │  PWA      │
└─────────────────────────────────────────────────────────┘'''))
    
    story.append(H2('3.2　数据流设计'))
    story.append(P('以下是用户从上传文件到开始游戏的完整数据流：'))
    
    story.append(Code('''用户上传 GOG zip
    │
    ▼
[1. 解压提取] ──→ Content/ 目录 (3550 XNB 文件, 544MB)
    │              Stardew Valley.dll (6.2MB, 已反编译预编译)
    ▼
[2. 加载 WASM] ──→ sdv-engine.wasm (预编译, ~15MB)
    │              sdv-engine.dll (反编译源码编译产物)
    ▼
[3. 初始化 VFS] ─→ HttpVfs 挂载 Content/ 到 /deps/content/
    │              SdvFileShim 重定向 File.Open → HttpVfs
    ▼
[4. 加载 SDV] ──→ AssemblyLoadContext.Default.LoadFromStream(sdv-engine.dll)
    │              KNI 程序集预加载 (patched)
    ▼
[5. 启动游戏] ──→ Program.Main() → GameRunner..ctor() → Run()
    │              Initialize() → LoadContent() (118+ XNB 加载)
    ▼
[6. 游戏循环] ──→ Tick() → Update() → Draw() → WebGL2 → Canvas
    │              输入: VirtualInput.ApplyToInput()
    │              存档: OPFS 持久化
    ▼
[用户游玩]'''))
    
    story.append(H2('3.3　组件职责'))
    story.append(make_table([
        ['组件', '位置', '职责'],
        ['BlazorWebAssembly 宿主', 'SdvBlazor/Pages/Home.razor', '浏览器入口，canvas 管理，PWA'],
        ['HttpVfs', 'SdvWebPort.Vfs', 'HTTP 获取 XNB 文件，同步 XHR (JSImport)'],
        ['SdvFileShim', 'SdvWebPort.Vfs', 'File/Directory 调用重定向到 VFS'],
        ['KniCompatShim', 'StardewValley.Decompiled/', '补全 KNI 缺失的类型和方法'],
        ['KniGamePatcher', 'SdvWebPort.Rewriter/', '修补 KNI Game 方法可见性'],
        ['KniGraphicsPatcher', 'SdvWebPort.Rewriter/', '修补 KNI GraphicsAdapter.IsProfileSupported'],
        ['VirtualInput', 'SdvBlazor/Input/', '统一 PC/移动端输入转换'],
        ['SDV 反编译源码', 'StardewValley.Decompiled/', '950 个 .cs 文件，原版游戏逻辑'],
        ['Content/ 资源', 'wwwroot/deps/content/', '3550 XNB 文件，游戏数据'],
    ], col_widths=[38*mm, 50*mm, 72*mm]))
    
    story.append(H2('3.4　反编译源码路线'))
    story.append(P('反编译源码路线的核心流程如下：'))
    
    story.append(Code('''原始 SDV.dll (GOG v1.6.15, 6.2MB)
    │
    │ ilspycmd -p (ILSpy 8.2, 项目模式)
    ▼
950 个 .cs 文件 (306,774 行 C# 代码)
    │
    │ 修复 API 不匹配:
    │   - 370 个 CS0012: 创建 MG facade type-forward 到 KNI
    │   - 48 个 CS1061: 在 KniCompatShim 补 stub 方法
    │   - 20 个 CS0103: 在 KniCompatShim 补 stub 类型
    │   - 34 个其他: 逐个修复
    ▼
dotnet build (net8.0, BlazorWebAssembly)
    │
    ▼
sdv-engine.dll + sdv-engine.wasm
    │
    │ 加载到 BlazorWebAssembly
    ▼
完整运行原版游戏'''))
    
    story.append(P('这个流程的关键特点是：'))
    story.append(Bullets([
        '<b>游戏逻辑零修改</b>：950 个 .cs 文件中的游戏逻辑代码完全保留，只修改 API 调用',
        '<b>适配层独立</b>：KniCompatShim 等适配代码独立于游戏版本，版本更新时不需要重新写',
        '<b>编译器友好</b>：C# 编译器生成的 IL 是标准的，不触发 WASM JIT 特殊路径',
        '<b>可调试</b>：编译错误明确，不像运行时崩溃那样难定位',
    ]))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 4: API ADAPTATION ==========
    story.append(H1('第四章　API 适配清单'))
    story.append(hr())
    
    story.append(H2('4.1　编译错误分类'))
    story.append(P(
        '反编译的 SDV 源码首次编译时产生 472 个错误。这些错误全部是 API 不匹配——'
        'SDV 依赖 MonoGame.Framework v3.6，而我们使用 KNI Framework 4.2（Xna.Framework.*）。'
        '错误可清晰分类，每类有明确的修复策略。'
    ))
    
    story.append(make_table([
        ['错误类型', '数量', '错误码', '修复方法', '难度'],
        ['类型转发缺失', '370', 'CS0012', '创建 MG facade → KNI', '中'],
        ['方法缺失', '48', 'CS1061', 'KniCompatShim 补 stub', '低'],
        ['类型缺失', '20', 'CS0103', 'KniCompatShim 补类型', '低'],
        ['参数不匹配', '8', 'CS1503', '适配调用参数', '低'],
        ['方法重载缺失', '6', 'CS1501', '补 stub 重载', '低'],
        ['其他', '20', 'CS0246 等', '逐个修复', '低'],
    ], col_widths=[35*mm, 18*mm, 22*mm, 50*mm, 15*mm]))
    
    story.append(callout(
        '关键区别：路线 A 的问题是"运行时随机崩溃，不知道为什么"，路线 B 的问题是"编译器明确告诉你哪里错了"。'
        '前者是玄学，后者是工程。',
        'info'
    ))
    
    story.append(H2('4.2　CS0012 类型转发修复'))
    story.append(P(
        '<b>问题</b>：SDV 的依赖（xTile, StardewValley.GameData）编译时引用 MonoGame.Framework v3.6，'
        '其中定义了 Rectangle、Vector2 等类型。当我们用 KNI 的 Xna.Framework 时，编译器找不到 v3.6 版本的类型定义。'
    ))
    story.append(P('<b>修复</b>：创建一个 MonoGame.Framework facade 程序集，将所有类型 type-forward 到 KNI：'))
    
    story.append(Code('''// MonoGame.Framework facade 项目 (MonoGame.Framework.Facade.csproj)
// 空实现，只有 TypeForwardedTo 属性

[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Vector2))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Rectangle))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Color))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Matrix))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Point))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Vector3))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Vector4))]
[assembly: TypeForwardedTo(typeof(Microsoft.Xna.Framework.Quaternion))]
// ... 337 个 TypeForwardedTo 属性

// 引用: Xna.Framework, Xna.Framework.Graphics, Xna.Framework.Game 等 KNI 程序集
// 编译为: MonoGame.Framework.dll (facade)'''))
    
    story.append(P('这样当 SDV 依赖查找 MonoGame.Framework 中的类型时，facade 会转发到 KNI 的实际实现。'))
    
    story.append(H2('4.3　缺失 API 补全'))
    story.append(P('KNI 相比 MonoGame.Framework 缺失一些 API，需要在 KniCompatShim 中补 stub：'))
    
    story.append(H3('音频 API（CueDefinition 等）'))
    story.append(P('SDV 使用 XACT 音频引擎的 CueDefinition、XactSound、NoAudioHardwareException 等类型，KNI 未实现。'))
    story.append(Code('''// KniCompatShim.cs - 音频 stub
namespace Microsoft.Xna.Framework.Audio
{
    public class CueDefinition
    {
        public string name;
        public List<XactSound> sounds = new();
        public void SetSound(byte[] data, int cat, bool loop, bool reverb) { }
        public Action OnModified;
    }
    
    public class XactSound { public byte[] data; public bool looped; }
    public class NoAudioHardwareException : Exception { ... }
}'''))
    
    story.append(H3('Game 方法可见性'))
    story.append(P('KNI 的 Game.Initialize/UnloadContent/Update/Draw 是 protected internal，'
        'SDV 的 GameRunner 在不同程序集中用 protected override 无法匹配。'))
    story.append(Code('''// KniGamePatcher.cs - 修补 KNI Game 方法可见性
var method = gameType.Methods.First(m => m.Name == "Initialize");
method.IsFamilyOrAssembly = false;  // protected internal
method.IsFamily = true;             // → protected
// 4 个方法: Initialize, UnloadContent, Update, Draw'''))
    
    story.append(H3('OnActivated 签名差异'))
    story.append(P('MonoGame 的 Game.OnActivated 接收 (object, EventArgs)，KNI 只接收 (EventArgs)。'))
    story.append(Code('''// 修改 GameRunner.cs (反编译源码)
// 原: protected override void OnActivated(object sender, EventArgs args)
// 改: protected override void OnActivated(EventArgs args)
//     instance.Instance_OnActivated(this, args);'''))
    
    story.append(H2('4.4　P/Invoke 处理'))
    story.append(P('SDV 有 23 处 P/Invoke（DllImport），WASM 不支持原生调用，需要用 stub 替代：'))
    
    story.append(make_table([
        ['P/Invoke 位置', '原功能', 'WASM stub'],
        ['LWJGL.LZ4', 'LZ4 压缩/解压', 'C# 实现 LZ4 或返回空'],
        ['DesktopClipboard', '系统剪贴板', 'JS Interop 调用 navigator.clipboard'],
        ['WindowsSdlClipboard', 'SDL 剪贴板', '同上，JS Interop'],
        ['KeyboardInput', 'Windows 键盘 hook', '空实现，WASM 用浏览器输入'],
        ['AudioEngine', 'XACT 音频引擎', 'Web Audio API 替代'],
        ['GameWindow', '窗口管理', 'KNI 已处理，stub 掉剩余'],
    ], col_widths=[40*mm, 45*mm, 60*mm]))
    
    story.append(P('处理策略：'))
    story.append(Bullets([
        '<b>剪贴板</b>：通过 JS Interop 调用浏览器 navigator.clipboard API',
        '<b>压缩</b>：用 C# 实现的 LZ4 替代（Ionic.Zlib 已在 SDV 中）',
        '<b>音频</b>：XACT → Web Audio API 转换层（Phase 5）',
        '<b>窗口/输入</b>：KNI 已处理大部分，剩余空实现',
    ]))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 5: MOBILE ==========
    story.append(H1('第五章　移动端方案'))
    story.append(hr())
    
    story.append(H2('5.1　虚拟摇杆设计'))
    story.append(P('SDV 的输入需求分为两类：菜单交互（天然触摸兼容）和游戏内操作（需要虚拟控件）。'))
    
    story.append(H3('输入需求分析'))
    story.append(make_table([
        ['操作类型', 'PC 键鼠', '移动端适配', '兼容性'],
        ['菜单/对话框', '鼠标点击', '直接触摸', '✅ 天然兼容'],
        ['角色移动', 'WASD/方向键', '虚拟摇杆', '需适配'],
        ['使用工具', '右键/空格', '虚拟按钮 A', '需适配'],
        ['背包切换', '数字键 1-12', '横向滑动栏', '需适配'],
        ['确认/取消', 'Enter/Esc', '虚拟按钮 B/A', '需适配'],
        ['右键菜单', '鼠标右键', '长按 500ms', '需适配'],
    ], col_widths=[30*mm, 30*mm, 40*mm, 25*mm]))
    
    story.append(callout(
        '60% 的操作天然兼容：SDV 的菜单系统全是 ClickableComponent，本身是点击式的，'
        '标题菜单、对话框、背包、地图等在移动端直接触摸就能用。',
        'success'
    ))
    
    story.append(H3('虚拟控件布局'))
    story.append(Code('''┌─────────────────────────────────┐
│ [1][2][3][4][5][6] ← 快捷栏    │
│                                 │
│        游戏画面区域              │
│                                 │
│                                 │
│ ┌──┐                      ┌──┐ │
│ │🎮│摇杆           A⚒️│ │
│ │  │             ┌──┐    │  │ │
│ └──┘            │ B↩│   └──┘ │
│                 └──┘          │
└─────────────────────────────────┘'''))
    
    story.append(H2('5.2　MobileAtlas 资源利用'))
    story.append(P(
        'Android APK 分析发现，Android 版 SDV 包含 PC 版没有的移动端 UI 资源：'
    ))
    
    story.append(make_table([
        ['资源文件', '用途', '来源'],
        ['MobileAtlas_manually_made.xnb', '虚拟摇杆/按钮精灵图', 'Android APK'],
        ['MobileAtlas_tencent.xnb', '腾讯中国版专用精灵图', 'Android APK'],
        ['JunimoNoteMobile.xnb', '移动端 Junimo 笔记 UI', 'Android APK'],
        ['JunimoNoteMobile_raccoon.xnb', '移动端浣熊 Junimo 笔记', 'Android APK'],
    ], col_widths=[55*mm, 50*mm, 40*mm]))
    
    story.append(P(
        '我们可以直接使用这些官方移动端 UI 资源，而不需要自己画虚拟摇杆的图形。'
        '这些资源已经从 Android APK 提取，存放在 download/android-assets/ 目录。'
    ))
    
    story.append(callout(
        'Android 版 SDV 是 NativeAOT 编译（.so 文件），无法反编译代码。'
        '但我们可以借用其移动端 UI 资源（XNB 精灵图），配合自己实现的触摸输入逻辑。',
        'info'
    ))
    
    story.append(H2('5.3　触摸输入转换层'))
    story.append(P('核心设计：VirtualInput 层将浏览器触摸事件转换为 SDV 的 MouseState/KeyboardState：'))
    
    story.append(Code('''// VirtualInput.cs - 统一输入层
public class VirtualInput
{
    // 摇杆状态 (-1.0 ~ 1.0)
    public Vector2 JoystickDirection { get; private set; }
    
    // 按钮状态
    public bool ActionButton { get; private set; }  // A: 使用工具
    public bool CancelButton { get; private set; }  // B: 取消
    public bool MenuButton { get; private set; }    // Y: 背包
    public bool TabButton { get; private set; }     // X: 切换工具
    
    // 注入到 KNI 的 MouseState/KeyboardState
    public void ApplyToInput(ref MouseState mouse, ref KeyboardState keyboard)
    {
        // 摇杆 → WASD
        if (JoystickDirection.Y < -0.5f) keyboard.SetKey(Keys.W);
        if (JoystickDirection.Y >  0.5f) keyboard.SetKey(Keys.S);
        if (JoystickDirection.X < -0.5f) keyboard.SetKey(Keys.A);
        if (JoystickDirection.X >  0.5f) keyboard.SetKey(Keys.D);
        
        // A 按钮 → 空格 (使用工具)
        if (ActionButton) keyboard.SetKey(Keys.Space);
        
        // B 按钮 → Esc (取消)
        if (CancelButton) keyboard.SetKey(Keys.Escape);
    }
}'''))
    
    story.append(P('这个设计的关键优势是：'))
    story.append(Bullets([
        '<b>游戏代码零修改</b>：不需要改 SDV 的输入代码，在 KNI Input 层注入虚拟输入',
        '<b>PC/移动端统一</b>：PC 透传键鼠，移动端触摸转虚拟键鼠，游戏代码不区分设备',
        '<b>响应式 UI</b>：检测设备类型，移动端显示虚拟控件，PC 端隐藏',
    ]))
    
    story.append(H3('设备检测与自适应'))
    story.append(Code('''// MobileDetector.cs
public bool IsMobileDevice =>
    OperatingSystem.IsBrowser() && 
    (JSRuntime.Invoke<string>("eval", 
        "navigator.userAgent.toLowerCase()").Contains("mobile")
     || HasTouchScreen());

// Home.razor
@if (IsMobileDevice)
{
    <VirtualJoystick OnMove="HandleJoystick" />
    <VirtualButtons OnAction="HandleAction" OnCancel="HandleCancel" />
}
<canvas id="theCanvas" />'''))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 6: TIMELINE ==========
    story.append(H1('第六章　时间线'))
    story.append(hr())
    
    story.append(H2('6.1　五阶段甘特图'))
    story.append(Code('''Phase 1  ████████░░░░░░░░░░░░░░░░░░░░  源码编译通过 (2-3天)
Phase 2  ░░░░░░░░████████░░░░░░░░░░░░  标题画面 (1周)
Phase 3  ░░░░░░░░░░░░░░░░████████░░░░  输入+移动端 (1周)
Phase 4  ░░░░░░░░░░░░░░░░░░░░░░░░████  基本可玩 (2-3周)
Phase 5  ░░░░░░░░░░░░░░░░░░░░░░░░░░██  完整体验 (持续)

Week:    1  2  3  4  5  6  7  8  9  10
         |--|--|--|--|--|--|--|--|--|'''))
    
    story.append(H2('6.2　里程碑与交付物'))
    
    story.append(H3('Phase 1: 源码编译通过 (2-3 天)'))
    story.append(make_table([
        ['任务', '产出', '验收标准'],
        ['创建 MG facade', 'MonoGame.Framework.Facade.dll', 'CS0012 错误清零'],
        ['补全 KniCompatShim', 'CueDefinition 等 stub', 'CS0246/CS0103 清零'],
        ['修 P/Invoke stub', '23 处 DllImport 替换', '无 DllImport 调用'],
        ['编译通过', 'sdv-engine.dll', 'dotnet build 0 错误'],
    ], col_widths=[45*mm, 50*mm, 55*mm]))
    
    story.append(H3('Phase 2: 标题画面 (1 周)'))
    story.append(make_table([
        ['任务', '产出', '验收标准'],
        ['Blazor 集成', 'SDV 加载到 BlazorWASM', 'Program.Main 执行'],
        ['VFS 重定向', 'HttpVfs 挂载 Content/', '118+ XNB 加载'],
        ['KNI Game patch', '方法可见性修复', 'GameRunner.Run() 成功'],
        ['完整标题画面', '云朵+logo+按钮渲染', '0 崩溃，5+ tick'],
    ], col_widths=[45*mm, 50*mm, 55*mm]))
    
    story.append(H3('Phase 3: 输入系统 + 移动端 (1 周)'))
    story.append(make_table([
        ['任务', '产出', '验收标准'],
        ['PC 键鼠输入', 'KeyboardState/MouseState', '可点击标题按钮'],
        ['移动端触摸', 'VirtualInput 层', '触摸操作标题画面'],
        ['虚拟控件 UI', '摇杆+按钮+快捷栏', 'MobileAtlas 精灵图'],
        ['响应式适配', '设备检测+全屏横屏', 'PC/Mobile 自适应'],
    ], col_widths=[45*mm, 50*mm, 55*mm]))
    
    story.append(H3('Phase 4: 基本可玩 (2-3 周)'))
    story.append(make_table([
        ['任务', '产出', '验收标准'],
        ['角色创建', '新建角色界面', '能输入名字、选外观'],
        ['游戏世界加载', '进入农场', '地图渲染、NPC 生成'],
        ['基本操作', '走动/使用工具/背包', 'PC+Mobile 都可操作'],
        ['存档系统', 'OPFS 持久化', '新建/读取存档'],
        ['基础交互', '种地/挖矿/对话', '前几分钟可玩'],
    ], col_widths=[45*mm, 50*mm, 55*mm]))
    
    story.append(H3('Phase 5: 完整体验 (持续)'))
    story.append(make_table([
        ['任务', '产出', '验收标准'],
        ['音频系统', 'XACT → Web Audio API', 'BGM+音效播放'],
        ['全部游戏内容', '所有季节/事件/NPC', '完整游戏流程'],
        ['多人联机', 'WebRTC P2P', '2+ 人联机'],
        ['Mod 支持', 'Harmony 适配', '加载 SMAPI mod'],
        ['PWA 离线', 'Service Worker', '离线可玩'],
    ], col_widths=[45*mm, 50*mm, 55*mm]))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 7: RISK ASSESSMENT ==========
    story.append(H1('第七章　风险评估'))
    story.append(hr())
    
    story.append(H2('7.1　技术风险'))
    story.append(make_table([
        ['风险', '概率', '影响', '应对措施'],
        ['编译错误超预期', '中', 'Phase 1 延期', '分类修复，每类有明确策略'],
        ['运行时新崩溃', '中', 'Phase 2 延期', '源码级调试，比 IL 可控'],
        ['KNI API 缺失多', '低', '补 stub 工作量大', 'KniCompatShim 框架已建立'],
        ['WASM 内存限制', '中', '大地图加载失败', '增量加载，优化资源'],
        ['性能不达标', '中', '游戏卡顿', 'AOT 编译，渲染优化'],
    ], col_widths=[40*mm, 15*mm, 30*mm, 65*mm]))
    
    story.append(H2('7.2　法律风险'))
    story.append(P(
        '<b>反编译的法律边界</b>：SDV 是商业游戏，反编译 EULA 通常禁止。'
        '但用户明确表示"不盈利、不公开、个人玩"，在此前提下法律风险基本为零：'
    ))
    story.append(Bullets([
        '反编译用于个人兼容性，多数法律有 interoperability 豁免',
        '不分发 = 不侵权',
        '用户自己买的游戏，在自己机器上跑',
        '仓库不包含反编译代码（.gitignore 排除）',
    ]))
    
    story.append(callout(
        '法律风险评级：极低。个人使用 + 不分发 + 不盈利 = 基本无风险。'
        '但项目仓库不应公开反编译的 SDV 源码，只提供工具链。',
        'success'
    ))
    
    story.append(H2('7.3　性能风险'))
    story.append(make_table([
        ['性能指标', '目标', '风险', '优化方案'],
        ['加载时间', '< 10s', '中', 'PWA 缓存，增量加载'],
        ['帧率 (PC)', '60 fps', '低', 'KNI WebGL2 已优化'],
        ['帧率 (移动)', '30 fps', '中', '降分辨率，限制帧率'],
        ['内存占用', '< 1GB', '中', '资源懒加载，及时释放'],
        ['WASM 大小', '< 20MB', '低', 'AOT + trim'],
    ], col_widths=[35*mm, 25*mm, 20*mm, 60*mm]))
    
    story.append(H3('性能优化策略'))
    story.append(Bullets([
        '<b>AOT 编译</b>：源码路线下 AOT 更可控，绕过 JIT 解释器性能损耗',
        '<b>PWA 缓存</b>：首次加载后，WASM + 资源缓存到 Service Worker，离线秒开',
        '<b>增量加载</b>：大地图按需加载，避免一次性加载全部资源',
        '<b>移动端降级</b>：降低渲染分辨率，限制 30fps，减少内存压力',
    ]))
    
    story.append(PageBreak())
    
    # ========== CHAPTER 8: USER EXPERIENCE ==========
    story.append(H1('第八章　用户端体验'))
    story.append(hr())
    
    story.append(H2('8.1　传 zip 即玩流程'))
    story.append(P('最终用户体验：上传游戏文件 → 自动处理 → 直接游玩。'))
    
    story.append(Code('''┌─────────────────────────────────────────────┐
│            SDV Web Port                     │
│                                             │
│   欢迎来到星露谷物语 Web 版！                │
│                                             │
│   请上传你的游戏文件：                       │
│   ┌─────────────────────────────────────┐  │
│   │     拖入 GOG zip 或 Android APK      │  │
│   │       或点击选择文件                 │  │
│   └─────────────────────────────────────┘  │
│                                             │
│   [开始游戏]                                │
│                                             │
│   首次加载约 2 分钟，之后离线可玩            │
└─────────────────────────────────────────────┘

后台自动处理：
1. 解压提取 Content/ (3550 XNB 文件)
2. 加载预编译的 sdv-engine.wasm (15MB)
3. 挂载 VFS，初始化游戏
4. 显示标题画面'''))
    
    story.append(H2('8.2　PWA 离线支持'))
    story.append(P('通过 PWA + Service Worker 实现离线可玩：'))
    
    story.append(make_table([
        ['阶段', '网络需求', '加载内容', '耗时'],
        ['首次使用', '需要网络', 'WASM (15MB) + 资源 (500MB)', '2-5 分钟'],
        ['后续使用', '完全离线', '从 Service Worker 缓存加载', '< 5 秒'],
        ['版本更新', '需要网络', '仅更新的文件', '取决于更新大小'],
    ], col_widths=[30*mm, 30*mm, 55*mm, 25*mm]))
    
    story.append(H3('Service Worker 缓存策略'))
    story.append(Code('''// sw.js - Service Worker
const CACHE_NAME = 'sdv-web-port-v2.0';
const CORE_ASSETS = [
  '/',
  '/index.html',
  '/sdv-engine.wasm',
  '/_framework/blazor.boot.json',
];

// 安装时缓存核心资源
self.addEventListener('install', e => {
  e.waitUntil(caches.open(CACHE_NAME)
    .then(cache => cache.addAll(CORE_ASSETS)));
});

// 游戏资源按需缓存
self.addEventListener('fetch', e => {
  if (e.request.url.includes('/deps/content/')) {
    e.respondWith(
      caches.open('sdv-content').then(cache =>
        cache.match(e.request).then(resp =>
          resp || fetch(e.request).then(resp => {
            cache.put(e.request, resp.clone());
            return resp;
          })
        )
      )
    );
  }
});'''))
    
    story.append(H2('8.3　跨设备体验'))
    story.append(P('同一套引擎，通过响应式 UI 适配不同设备：'))
    
    story.append(H3('PC 体验'))
    story.append(Bullets([
        '键盘 WASD 移动，鼠标点击交互',
        '数字键 1-12 切换快捷栏',
        '空格使用工具，E 打开背包',
        '全屏支持，可窗口化',
    ]))
    
    story.append(H3('移动端体验'))
    story.append(Bullets([
        '左下角虚拟摇杆移动',
        '右下角 A/B/X/Y 动作按钮',
        '顶部快捷栏横向滑动',
        '长按 = 右键',
        '全屏横屏锁定',
        'PWA 安装到主屏幕，原生 App 体验',
    ]))
    
    story.append(H3('存档同步'))
    story.append(P(
        '存档使用 OPFS（Origin Private File System）持久化，浏览器关闭后不丢失。'
        '存档与设备绑定（不跨设备同步，除非用户手动导出/导入）。'
    ))
    
    story.append(PageBreak())
    
    # ========== APPENDIX ==========
    story.append(H1('附录'))
    story.append(hr())
    
    story.append(H2('A. 环境配置'))
    story.append(Code('''# .NET 8 SDK
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
dotnet workload install wasm-tools

# KNI Framework 4.2
# 已包含在项目 NuGet 配置中

# ILSpy 反编译工具
dotnet tool install -g ilspycmd --version 8.2.0.7535

# Playwright (headless 测试)
npm install -g playwright
playwright install chromium

# 项目依赖
cd /home/z/my-project
dotnet restore'''))
    
    story.append(H2('B. 项目结构'))
    story.append(Code('''sdv-web-port/
├── src/
│   ├── StardewValley.Decompiled/     # 反编译的 SDV 源码 (950 .cs)
│   │   ├── Stardew Valley.csproj     # 编译项目
│   │   ├── KniCompatShim.cs          # KNI 兼容层
│   │   └── StardewValley/            # 游戏代码
│   ├── SdvWebPort.PoC.SdvBlazor/     # Blazor 宿主
│   │   ├── Pages/Home.razor          # 主页面
│   │   ├── SdvLoader.cs              # SDV 加载器
│   │   ├── Input/                    # 虚拟输入层
│   │   └── wwwroot/                  # 静态资源
│   ├── SdvWebPort.Rewriter/          # KNI patcher
│   │   ├── KniGamePatcher.cs
│   │   ├── KniGraphicsPatcher.cs
│   │   └── SdvAssemblyRefRewriter.cs
│   └── SdvWebPort.Vfs/               # 虚拟文件系统
│       ├── HttpVfs.cs
│       └── SdvFileShim.cs
├── scripts/
│   ├── decompile-sdv/                # 反编译工具
│   └── gen-sdv-plan.py               # 本文档生成脚本
├── download/
│   └── android-assets/               # Android APK 提取资源
└── docs/'''))
    
    story.append(H2('C. 关键技术决策记录'))
    story.append(make_table([
        ['决策点', '选择', '理由', '日期'],
        ['运行时', '.NET 8 BlazorWASM', 'KNI 官方支持', '2026-07-03'],
        ['图形 API', 'KNI 4.2 (WebGL2)', 'XNA→WebGL2 桥接', '2026-07-03'],
        ['路线选择', 'B (反编译源码)', '绕过 WASM JIT 限制', '2026-07-11'],
        ['基础版本', 'PC GOG (非 Android)', 'PC 是 .NET 托管可反编译', '2026-07-11'],
        ['移动端资源', '借用 Android MobileAtlas', '官方移动端 UI 精灵图', '2026-07-11'],
        ['输入方案', '统一 VirtualInput 层', 'PC/Mobile 共用引擎', '2026-07-11'],
        ['存档方案', 'OPFS', '浏览器原生持久化', '2026-07-11'],
    ], col_widths=[30*mm, 40*mm, 55*mm, 25*mm]))
    
    story.append(H2('D. 参考资料'))
    story.append(Bullets([
        'KNI Framework: https://github.com/kniEngine/kni',
        'ILSpy: https://github.com/icsharpcode/ILSpy',
        'BlazorWebAssembly: https://learn.microsoft.com/blazor/webassembly',
        'Mono.Cecil: https://github.com/jbevain/cecil',
        'Web Audio API: https://developer.mozilla.org/Web/API/Web_Audio_API',
        'OPFS: https://developer.mozilla.org/Web/API/File_System_Access_API',
        'PWA: https://web.dev/progressive-web-apps/',
    ]))
    
    return story

# ============================================================
# MAIN
# ============================================================
def main():
    output = '/home/z/my-project/download/SDV-Web-Port-技术方案计划书.pdf'
    
    doc = SdvDocTemplate(
        output,
        pagesize=A4,
        title='SDV Web Port 技术方案计划书',
        author='SDV-Web-Port Team',
        subject='星露谷物语 Web 移植技术方案',
        creator='Z.ai',
    )
    
    story = build_story()
    
    # Build body
    body_pdf = '/tmp/sdv-body.pdf'
    doc.build(story)
    
    # Note: doc.build already writes to output, but we need to merge cover
    # Actually, let's build body to temp, then merge
    print(f'[+] Body PDF generated: {output}')
    
    # Merge cover + body
    from pypdf import PdfReader, PdfWriter
    
    cover_reader = PdfReader('/tmp/cover.pdf')
    body_reader = PdfReader(output)
    
    writer = PdfWriter()
    for page in cover_reader.pages:
        writer.add_page(page)
    for page in body_reader.pages:
        writer.add_page(page)
    
    # Add metadata
    writer.add_metadata({
        '/Title': 'SDV Web Port 技术方案计划书',
        '/Author': 'SDV-Web-Port Team',
        '/Subject': '星露谷物语 Web 移植技术方案 v2.0',
        '/Creator': 'Z.ai',
    })
    
    final_output = '/home/z/my-project/download/SDV-Web-Port-技术方案计划书.pdf'
    with open(final_output, 'wb') as f:
        writer.write(f)
    
    print(f'[+] Final PDF: {final_output}')
    print(f'[+] Total pages: {len(cover_reader.pages) + len(body_reader.pages)}')
    
    import os
    size = os.path.getsize(final_output)
    print(f'[+] File size: {size / 1024:.1f} KB')

if __name__ == '__main__':
    main()
