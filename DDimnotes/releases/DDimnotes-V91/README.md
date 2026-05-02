## V33 Data validation and settings dialog cleanup

- Version: `1.0.33.0`.
- Removed the Data section from the settings dialog.
- `Ghi chú` is now section 6.
- `MIS_SET.SET` is required from AutoCAD Support File Search Path.
- `ti.dat` is required only when selected objects contain metric thread M holes.
- Missing runtime data is reported safely and the command returns without uncaught exceptions.
- `UserData` remains reference/maintenance only.

## V32 Settings dialog width alignment

- Các ô nhập/list/editbox trong hộp thoại Settings dùng cùng chiều rộng với ô list Chọn chế độ.
- Các ô này căn cùng mép phải để mục 3/4/5 thẳng cột với mục 1/2.
- Version: `1.0.33.0`.

# DDimnotes V40

DDimnotes V20 gộp logic DNOTES vào lệnh DDimnotes: một lần chạy sẽ vẽ hình chiếu cạnh, tạo dim, viết Notes và đánh mark Notes.

## Cấu trúc gói

Khi tải ZIP về, trong thư mục gốc chỉ giữ 3 thư mục: `Log`, `Reference`, `UserData`. Các file code/helper đặt trực tiếp ở thư mục gốc để dễ kiểm tra và bảo dưỡng.

## Lệnh AutoCAD

- `DDimnotes`

## Prompt chính

```text
Nhập cỡ chữ dim hoặc (S=Cài đặt) (270):<cỡ chữ đã lưu hoặc DIMTXT>:
```

Trong đó:

- `D/B/A` là mode đo/vẽ hiện hành.
- `270` là góc chiếu hiện hành, mặc định 270 độ khi mở bản vẽ.
- Nhập `S` để cài đặt thêm.

## Cài đặt S

```text
D = Chiều sâu + Đường bao
B = Đo đường bao
A = Tất cả
R = Tỉ lệ/width factor chữ Notes
C = Màu chữ Notes
L = Tỉ lệ khoảng cách đặt bảng Notes
G = Góc chiếu, chỉ nhận 0/90/180/270
```

## Vị trí Notes

Notes được chèn tự động tại:

```text
X = Xmax hình chính + khoảng cách giữa hình chính và side view * L
Y = Ymax hình chính
```

`L` mặc định là `1.0` cho bản vẽ đang mở.

## Ghi chú dữ liệu

- `UserData/ti.dat` chỉ là file tham khảo/bảo dưỡng để biết code cần dữ liệu ren nào.
- `UserData/MIS_SET.SET` chỉ là file tham khảo/bảo dưỡng để biết DNOTES/Notes cần cấu hình nào.
- Khi chạy trong AutoCAD, code đọc `ti.dat` và `MIS_SET.SET` bằng AutoCAD Support File Search Path qua `FindFile(...)`.
- Không copy `UserData` vào thư mục output khi build.

## Build PowerShell

```powershell
cd "E:\ABC\C\Dmanage\Dmanage"
msbuild .\DDimnotes.sln /t:Restore
msbuild .\DDimnotes.sln /p:Configuration=Release /p:Platform="Any CPU"
```

DLL sau khi build:

```text
bin\DDimnotes.dll
```

Không tạo thêm thư mục `bin\Debug` hoặc `bin\Release`.

## V15 update

- Base: V14.
- Gộp phần lõi DNOTES vào DDimnotes.
- Không thêm lệnh DNOTES/DNOTES1 mới.
- Giữ logic V14: lỗ đồng tâm chỉ ghép bậc khi lỗ nhỏ hơn có dấu `+`.
- Version: `1.0.33.0`.


## V19 update

- Dim override theo DTE Lisp cho các giá trị DIM còn lại.
- Màu dim line/extension line lấy dòng 27 MIS_SET.SET; màu chữ dim lấy dòng 26 MIS_SET.SET.
- Không override DIMTXSTY/Dimtxsty; text style giữ theo DimStyle hiện hành.
- Reflection set property không phân biệt hoa/thường để Dimclrd/Dimclre/Dimclrt áp dụng chắc hơn.

## V17 update
- Dim vẫn dùng DimStyle hiện hành làm nền.
- Chỉ override DIMTXT theo cỡ chữ nhập.
- DIMCLRD và DIMCLRE lấy từ MIS_SET.SET dòng 27.
- DIMCLRT lấy từ MIS_SET.SET dòng 26.
- DIMASZ = DIMTXT / 3.

## V19 update

- Fixed dim text style behavior: generated dimensions now use the text style stored inside the current DimStyle, not AutoCAD current TEXTSTYLE.
- Other DTE-style overrides are kept.
- Line color remains from `MIS_SET.SET` line 27; dim text color remains from line 26.

## V20 update

- Chuẩn hóa tạo dim theo AutoCAD .NET: `Database.GetDimstyleData` -> sửa bản copy `DimStyleTableRecord` -> `Dimension.SetDimstyleData`.
- Không dùng reflection để ép `Dimtxsty/TextStyleId` nữa.
- Text style của dim giữ theo DIM style hiện hành/effective dim style data, không lấy theo current `TEXTSTYLE`.
- Màu dim line/extension line lấy dòng 27 `MIS_SET.SET`; màu chữ dim lấy dòng 26.
- Các giá trị DTE còn lại áp trên `DimStyleTableRecord` copy, không đổi global DIM variables của bản vẽ.

## V23 Settings dialog

When the command prompts for text height, enter `S` to open the DDimnotes settings dialog.
The dialog is grouped into:

- Mode: D / B / A
- Projection angle: 0 / 90 / 180 / 270
- Notes: create notes, create marks, note text width factor, note text color, note distance scale
- Current Dim: read-only information from current dimension style and MIS_SET.SET colors
- Data status section removed from settings dialog; missing data is reported safely when command runs

Settings are stored only for the current open drawing/session. Closing and reopening the drawing resets the default mode to D and projection angle to 270.

## V31 Notes gap scale

- Mode list shows Vietnamese names only: `Chiều sâu + Đường bao`, `Đo đường bao`, `Tất cả`.
- Internal command keywords remain D/B/A, but they are not shown in the dialog list.
- Spacing settings are split:
  - `Tỉ lệ đặt hình chiếu cạnh (so với cỡ chữ dim)` controls side-view gap as `scale * DIMTXT`.
  - `Tỉ lệ đặt Notes (so với cỡ chữ dim)` controls note placement as `NotesScale * dimTextHeight`.
- Dialog layout was enlarged and regrouped so Notes, Dim, and Data rows are not clipped.
- Version: `1.0.33.0`.


## V31 Notes gap scale

- Sửa logic đặt Notes: `NotesGap = NotesScale * dimTextHeight`.
- Khoảng cách Notes độc lập với khoảng cách hình chiếu cạnh, không nhân với side-view gap nữa.
- Tỉ lệ đặt hình chiếu cạnh và tỉ lệ đặt Notes đều tính trực tiếp theo cỡ chữ dim.
- Cập nhật nhãn hộp thoại: `Tỉ lệ đặt Notes (so với cỡ chữ dim)`.
- Version: `1.0.33.0`.

## V31 Settings dialog and Notes anchor
- Dialog width reduced by about one third and layout recalculated by row height.
- Section 4 is now “Khoảng cách bố trí (tỉ lệ so với chiều cao chữ dim)”.
- Added Notes insert position list:
  - `Xmax,Ymax`: `X = Xmax + NotesScale * DimTextHeight`, `Y = Ymax`.
  - `Xmin,Ymin`: `X = Xmin`, `Y = Ymin - SideViewGap - Thickness - NotesScale * DimTextHeight`.
- Version: `1.0.33.0`.

## V31 Notes anchor and two-axis offset
- Notes insert point now uses the union extents of MAIN + created side view, so it follows the projection angle 0/90/180/270.
- Notes offset is split into X and Y scale values, both relative to DIM text height.
- Settings defaults:
  - Xmax,Ymax: X = 4, Y = 0.
  - Xmin,Ymin: X = 0, Y = 4; positive Y is applied outward toward Y-, negative Y reverses direction.
- Version: `1.0.33.0`.

## V31 Dim offset setting
- Added Dim offset scale in settings dialog section 5.
- Formula: DimOffset = scale x dim text height.
- Default scale remains 3.00, matching the previous behavior.
- Removed the read-only dim text height row from section 5.
- Version: 1.0.33.0.


## V34 User settings INI
- Added persistent user settings file: `DUser\DDimnotes_settings.ini`.
- Runtime lookup uses AutoCAD Support File Search Path. If `DUser` is not found, DDimnotes warns the user and uses built-in defaults for that run.
- The file is stored as plain numeric lines only; list values are saved as indexes, not display text.
- Values from `MIS_SET.SET` and `ti.dat` are not stored in this settings file.
- Pressing `Reset mặc định` restores the current built-in defaults; pressing OK writes those values to the INI if `DUser` is available.
- Version: 1.0.34.0.

## V36 Language standard

- Main prompt no longer shows the mode keyword in parentheses; Settings dialog controls mode by list.
- Language handling follows the shared standard:
  - MIS_SET.SET line 28 selects the language id.
  - lang.dat provides Lang_X(FullName_ShortName) blocks.
  - Lang_0(English_En) is the fixed fallback.
  - UI/prompt/message strings use Lang.T/Lang.F keys.
- Superseded by V37: lang.dat is now build-time embedded resource, not runtime lookup data.
- Version: 1.0.36.0.

## V36 Language standard V2
- `Lang.cs` was rebuilt according to `Xu_ly_du_lieu_ngon_ngu_chuan_V2.txt`.
- Superseded by V37: runtime no longer searches for `lang.dat` on disk.
- Superseded by V37: `UserData/lang.dat` is embedded into the DLL at build time.
- Version: 1.0.36.0.

## V37 Build-time language standard
- Updated version to 1.0.37.0.
- `UserData/lang.dat` is now an `EmbeddedResource` in `DDimnotes.csproj` and is embedded into the DLL at build time.
- Runtime no longer searches for `lang.dat` on disk.
- Runtime language selection uses only `MIS_SET.SET` line 28. Value `X` selects block `Lang_X(FullName_ShortName)` from the embedded `lang.dat`.
- `Lang_0(English_En)` remains the fixed fallback.
- `Lang.BeginCommand()` and `Lang.Reload()` are called at the start of the `DDimnotes` command so `MIS_SET.SET` line 28 is reread for every command run.
- `Entry.Initialize()` no longer reads language data at NETLOAD time.
- `NtLang.Init()` is now a compatibility no-op; business/notes code does not reload or map language by itself.
- Replaced the obsolete runtime-language standard reference files in `UserData` with `Quy_chuan_xu_ly_da_ngon_ngu_AutoCAD_CSharp_BuildTime.txt`.
- Fixed an extra-brace syntax error in `NoteLogic.TryReadColorsFactor`.
- Version: 1.0.37.0.


## V38 Complete 4-language lang.dat
- Updated version to 1.0.38.0.
- Completed `UserData/lang.dat` for all four embedded language blocks: `Lang_0(English_En)`, `Lang_1(Vietnamese_Vn)`, `Lang_2(Chinese_Cn)`, and `Lang_3(Japan_Jp)`.
- Verified all four language blocks use the same key list so Chinese and Japanese do not fallback line-by-line for current UI/prompt/message keys.
- Kept the V37 build-time language rule: `lang.dat` is embedded into the DLL; runtime only reads `MIS_SET.SET` line 28.
- Version: 1.0.38.0.


## V39 Persist prompt DIM text height
- Updated version to 1.0.39.0.
- Added `DimTextHeight` as line 11 in `DUser\DDimnotes_settings.ini`.
- When the user enters a valid DIM text height directly at the main prompt, DDimnotes now saves that value immediately.
- The next command run uses the saved DDimnotes text height as the prompt default.
- Existing settings files remain compatible: if line 11 is missing or invalid, the command falls back to current AutoCAD `DIMTXT`.
- Version: 1.0.40.0.

## V40 No Notes mode
- Updated version to 1.0.40.0.
- Added mode `N` = No Notes.
- Mode `N` keeps the side-view and dimension behavior aligned with mode `D`, but does not create the Notes table or Note marks.
- Added `Form_ModeNoNotes` to all 4 embedded language blocks in `UserData/lang.dat`.
- Settings mode index now supports `3 = N`; old indexes remain `0=D`, `1=B`, `2=A`.

## V41 set_screw BSPR handling
- Updated version to 1.0.41.0.
- Added dedicated set_screw handling for codes such as `set_screwBSPR`.
- set_screw now reads MSW data from `ti.dat`: the calculated core/tap drill is drawn through the full plate thickness, while the nominal M diameter follows the signed depth.
- Negative depth is drawn from the back face.
- The thread-depth end keeps the angled/peak style used by normal metric thread drawing, not a flat step face.
- Added BSPR tag so Notes do not fall through to SPR/Spring.

## V47 set_screw no-extend correction
- Based on accepted V41 set_screwBSPR logic.
- Updated assembly version to 1.0.47.0.
- For set_screw, the nominal diameter depth segment no longer uses the normal metric-thread long root extension.
- The through core hole remains unchanged.
- The nominal diameter now stops at the requested depth and tapers inward to the core hole inside that depth.

## V48 set_screw drawing aligned with m_screw
- Built from V47/V41 accepted set_screwBSPR baseline.
- Updated assembly version to 1.0.48.0.
- Reverted the V47 dedicated set_screw taper/no-extend routine.
- set_screw nominal diameter now uses the same `ThreadFeaturePolylines` drawing style as normal `m_screw`.
- The set_screw core/tap-drill hole remains a separate through hole across the full plate thickness.
- The through core hole is not drawn as a pointed/tapered thread end.

## V49 set_screw remove extra small through-hole

- Built from the V48/V41 accepted set_screwBSPR baseline.
- Updated assembly version to 1.0.49.0.
- Kept the large/nominal set_screw diameter drawing aligned with normal `m_screw`.
- Removed the additional separate core/tap-drill `HoleSegRects` through-hole for set_screw.
- This prevents set_screw from showing three visible layers: nominal diameter + m_screw root line + extra small through-hole.


## V50 set_screw correct through-hole geometry

- Version: 1.0.50.0.
- Corrects V49: restores the through drill/core hole and removes only the small/root sharp-end polyline for set_screw.
- Keeps the large nominal set_screw contour drawn like m_screw.
- Normal m_screw drawing is unchanged.

## V51 side-face metric thread from Line centerline

- Version: 1.0.51.0.
- Base: V50 accepted set_screwBSPR logic.
- Added separate handling for metric-thread XData attached to a Line entity.
- The Line is treated as a side-face thread centerline, not as a front-view Circle hole.
- XData depth is ignored for this Line case; the Line segment itself controls the side-thread length.
- Line-based side threads are not grouped with normal holes, not filtered by representative circle-hole logic, and not merged with the existing m_screw / M_SCREW front-view path.

## V52 side-thread projection guard

- Base: V51 side-face metric thread from Line centerline.
- Added plan-range validation before drawing side-face Line threads.
- Clamped generated helper points of side-face thread symbols to the MAIN side-view thickness range.
- Preserved the rule that side-face Line thread data ignores XData depth.
- Preserved normal Circle/front-view hole logic and accepted set_screwBSPR behavior.

## V53 Line-thread end-view symbol

- Base: V52.
- For source entity `Line` with metric-thread XData, drawing is now separated from normal hole/thread geometry.
- If the Line direction matches the selected view direction group within 5 degrees, V53 draws a thread end-view symbol:
  - inner full circle diameter = D thread - pitch
  - outer partial arc diameter = D thread
  - centerlines total length = 2.2D
- If the Line direction does not match the selected view direction group, V53 ignores that Line thread.
- If pitch is missing from `ti.dat`, V53 reports a warning and draws with fallback pitch = 1.
- Non-Line holes/threads and set_screwBSPR are unchanged.

## V54 Line-thread entity output and centerline length fix

- Builds on V53 and keeps the Line + XData thread branch separated from normal Circle/Polyline hole/thread handling.
- For Line + metric thread XData that matches the selected view direction group within 5 degrees:
  - The inner thread circle is now created as a real AutoCAD `Circle`, not a polyline approximation.
  - The outer thread symbol is now created as a real AutoCAD `Arc`, not a polyline approximation.
  - Centerlines are created as real AutoCAD `Line` entities on CENTER linetype.
  - Total centerline length is fixed to `1.2 * D`.
    Example: D = 16 gives total centerline length = 19.2.
  - The open gap of the outer arc is exactly one quadrant of the circle and rotates by view direction:
    - 0 degrees: gap 0..90
    - 90 degrees: gap 90..180
    - 180 degrees: gap 180..270
    - 270 degrees: gap 270..360
- If the Line direction does not match the selected view direction group, it is still ignored.
- If pitch is missing from `ti.dat`, the user is warned and pitch = 1 is still used so drawing can continue.

## V57 Normal metric thread small-hole correction

- Version: 1.0.58.0.
- Built from the V54 baseline; the incorrect V55/V56 normal-thread changes are not used.
- Normal metric thread holes now use the user's three-case rule:
  - `L <= T`: draw normally.
  - `L > T` and `D <= T`: keep the large/outer thread contour normal, but draw the small/root hole (`nominal diameter - pitch`) as a through hole and remove its end peak.
  - Other cases: draw normally.
- Line + XData side-thread symbols and set_screw/MSW logic are unchanged.

## V59 - Cải tiến bảng cài đặt

V59 giữ nền V58 cho logic lỗ ren thường, Line + XData ren và set_screwBSPR/MSW. Thay đổi chính nằm ở form `DDimnotesSettingsForm` và file `DUser/DDimnotes_settings.ini`.

### Cấu trúc form

- A. Chế độ xử lý: dùng checkbox thay cho combo cũ.
- B. Cài đặt: cột trái gồm SIDE và DIM; cột phải là NOTES.

### Dữ liệu trong `DDimnotes_settings.ini`

File vẫn lưu theo dạng mỗi dòng một giá trị để tương thích cách đọc cũ. 12 dòng đầu giữ ý nghĩa của bản cũ; V59 bổ sung các dòng phía sau cho form mới.


## V60 - Việt hóa hộp thoại và bổ sung cài đặt Mark/Dim

- Giữ nền V59, không thay đổi logic lỗ ren V58/V59.
- Việt hóa nhãn hộp thoại: Notes -> Ghi chú, Mark Notes -> Ký hiệu ghi chú, DIM / KÍCH THƯỚC -> ĐO KÍCH THƯỚC.
- Bỏ hậu tố "x cỡ chữ" sau từng ô; dùng ghi chú chung: Các tỉ lệ dựa trên cỡ chữ.
- Cột HÌNH CHIẾU có Tự động / Tùy chỉnh cho vị trí đặt hình chiếu.
- Cột ĐO KÍCH THƯỚC có thêm Xóa trùng kích thước.
- Nhóm KÝ HIỆU GHI CHÚ đưa các giá trị cố định ra bảng: kiểu đặt, góc bắt đầu, bước xoay, số lần thử, khoảng cách, bước nhảy vòng và khoảng cách tối thiểu.
- Bổ sung settings dòng 31-36 cho các mục mới.

## V62 - Fix Vector3d.Zero compile error

- Built from V61.
- Fixed AutoCAD .NET compile error CS0117 where `Vector3d.Zero` is not available in the target AutoCAD API.
- Replaced with `new Vector3d(0.0, 0.0, 0.0)` in the custom side-view DrawJig move logic.
- No behavior changes to side-view custom placement, thread drawing, normal threaded holes, or set_screwBSPR/MSW.
