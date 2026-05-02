using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace DDimnotes
{
    internal sealed class DDimnotesSettingsForm : Form
    {
        public const string AnchorXmaxYmax = "XmaxYmax";
        public const string AnchorXminYmin = "XminYmin";
        public const string AnchorXminYmax = "XminYmax";
        public const string AnchorXmaxYmin = "XmaxYmin";
        public const string AnchorPickPoint = "PickPoint";

        private const int FormWidth = 860;
        private const int Margin = 14;
        private const int Gap = 8;
        private const int RowTop = 22;
        private const int RowHeight = 23;
        private const int RowPitch = 27;
        private const int RowLeft = 12;
        private const int RightPad = 12;
        private const int LabelGap = 8;
        private const int SectionHeaderHeight = 22;
        private const int ProcessingColumnGap = 16;
        private const int SettingsColumnGap = 14;
        private const int SettingsColumnWidth = (FormWidth - Margin * 2 - SettingsColumnGap) / 2;
        private const int SmallWidth = 145;
        private const int TolWidth = 58;
        private const int ColorWidth = 36;

        private readonly CheckBox _boundaryXminYmin;
        private readonly CheckBox _boundaryXminYmax;
        private readonly CheckBox _boundaryXmaxYmin;
        private readonly CheckBox _boundaryXmaxYmax;
        private readonly CheckBox _outerProfileVertices;

        private readonly CheckBox _holeCenters;
        private readonly CheckBox _arcCenters;
        private readonly CheckBox _blindHoles;
        private readonly CheckBox _innerProfiles;
        private readonly CheckBox _sideThickness;
        private readonly CheckBox _sideProjectionHoles;
        private readonly CheckBox _sideProjectionDepths;

        private readonly TextBox _tolBoundaryXminYmin;
        private readonly TextBox _tolBoundaryXminYmax;
        private readonly TextBox _tolBoundaryXmaxYmin;
        private readonly TextBox _tolBoundaryXmaxYmax;
        private readonly TextBox _tolOuterProfileVertices;
        private readonly TextBox _tolHoleCenters;
        private readonly TextBox _tolArcCenters;
        private readonly TextBox _tolBlindHoles;
        private readonly TextBox _tolInnerProfiles;
        private readonly TextBox _tolSideThickness;
        private readonly TextBox _tolSideProjectionHoles;
        private readonly TextBox _tolSideProjectionDepths;

        private readonly ComboBox _angleCombo;
        private readonly ComboBox _sidePlacementCombo;
        private readonly NumericUpDown _sideViewDistanceScale;

        private readonly ComboBox _boundaryDimModeCombo;
        private readonly NumericUpDown _dimTextScale;
        private readonly NumericUpDown _dimOffsetScale;
        private readonly CheckBox _deleteDuplicateDim;
        private readonly NumericUpDown _dimLineColor;
        private readonly NumericUpDown _dimTextColor;
        private readonly Panel _dimLinePreview;
        private readonly Panel _dimTextPreview;

        private readonly CheckBox _createNotes;
        private readonly ComboBox _noteTextStyleCombo;
        private readonly NumericUpDown _noteWidthFactor;
        private readonly NumericUpDown _noteColor;
        private readonly Panel _noteColorPreview;
        private readonly ComboBox _noteInsertPosition;
        private readonly NumericUpDown _noteOffsetXScale;
        private readonly NumericUpDown _noteOffsetYScale;

        private readonly CheckBox _createMarks;
        private readonly ComboBox _markTextStyleCombo;
        private readonly CheckBox _noteWireHole;
        private readonly CheckBox _noteThreadPitch;
        private readonly NumericUpDown _markTextScale;
        private readonly NumericUpDown _markColor;
        private readonly Panel _markColorPreview;
        private readonly ComboBox _markPositionCombo;
        private readonly NumericUpDown _markStartAngleDeg;
        private readonly NumericUpDown _markAngleStepDeg;
        private readonly NumericUpDown _markRotateTries;
        private readonly NumericUpDown _markDistanceScale;
        private readonly NumericUpDown _markRingStepScale;
        private readonly NumericUpDown _markMinDistanceScale;

        private readonly ComboBox _sideOutlineLayerCombo;
        private readonly ComboBox _sideViewLayerCombo;
        private readonly ComboBox _noteLayerCombo;
        private readonly ComboBox _markLayerCombo;
        private readonly ComboBox _dimLayerCombo;

        private bool _loading;

        private sealed class TextStyleOption
        {
            public string Value { get; private set; }
            public string DisplayName { get; private set; }

            public TextStyleOption(string value, string displayName)
            {
                Value = value ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        public sealed class DimensionModeOptions
        {
            public bool BoundaryXminYminDims { get; set; }
            public bool BoundaryXminYmaxDims { get; set; }
            public bool BoundaryXmaxYminDims { get; set; }
            public bool BoundaryXmaxYmaxDims { get; set; }
            public bool BoundaryCornerDims
            {
                get { return BoundaryXminYminDims || BoundaryXminYmaxDims || BoundaryXmaxYminDims || BoundaryXmaxYmaxDims; }
            }
            public bool OuterProfileVertexDims { get; set; }
            public bool HoleCenterDims { get; set; }
            public bool ArcCenterDims { get; set; }
            public bool DepthHoleDims { get; set; }
            public bool InnerProfileDims { get; set; }
            public bool SideThicknessDims { get; set; }
            public bool SideProjectionHoleDims { get; set; }
            public bool SideProjectionDepthDims { get; set; }
        }

        public DimensionModeOptions Mode { get; private set; }

        public string TolBoundaryXminYmin { get; private set; }
        public string TolBoundaryXminYmax { get; private set; }
        public string TolBoundaryXmaxYmin { get; private set; }
        public string TolBoundaryXmaxYmax { get; private set; }
        public string TolOuterProfileVertices { get; private set; }
        public string TolHoleCenters { get; private set; }
        public string TolArcCenters { get; private set; }
        public string TolBlindHoles { get; private set; }
        public string TolInnerProfiles { get; private set; }
        public string TolSideThickness { get; private set; }
        public string TolSideProjectionHoles { get; private set; }
        public string TolSideProjectionDepths { get; private set; }

        public int ProjectionAngle { get; private set; }
        public bool RequestCustomProjectionAngle { get; private set; }
        public int SidePlacementModeIndex { get; private set; }
        public double SideViewDistanceScale { get; private set; }

        public int BoundaryDimModeIndex { get; private set; }
        public double DimTextScale { get; private set; }
        public double DimOffsetScale { get; private set; }
        public bool DeleteDuplicateDim { get; private set; }
        public short DimLineColorIndex { get; private set; }
        public short DimTextColorIndex { get; private set; }

        public bool CreateNotes { get; private set; }
        public bool CreateNoteMarks { get; private set; }
        public bool NoteWireHole { get; private set; }
        public bool NoteThreadPitch { get; private set; }
        public string NoteTextStyleOption { get; private set; }
        public string MarkTextStyleOption { get; private set; }
        public double NoteWidthFactor { get; private set; }
        public short NoteTextColorIndex { get; private set; }
        public string NoteInsertPositionKey { get; private set; }
        public double NoteOffsetXScale { get; private set; }
        public double NoteOffsetYScale { get; private set; }

        public double MarkTextScale { get; private set; }
        public short MarkColorIndex { get; private set; }
        public int MarkPlacementModeIndex { get; private set; }
        public double MarkStartAngleDeg { get; private set; }
        public double MarkAngleStepDeg { get; private set; }
        public int MarkRotateTries { get; private set; }
        public double MarkDistanceScale { get; private set; }
        public double MarkRingStepScale { get; private set; }
        public double MarkMinDistanceScale { get; private set; }

        public string SideOutlineLayerOption { get; private set; }
        public string SideViewLayerOption { get; private set; }
        public string NoteLayerOption { get; private set; }
        public string MarkLayerOption { get; private set; }
        public string DimLayerOption { get; private set; }

        public DDimnotesSettingsForm(
            bool drawBoundaryXminYmin,
            bool drawBoundaryXminYmax,
            bool drawBoundaryXmaxYmin,
            bool drawBoundaryXmaxYmax,
            bool drawOuterProfileVertices,
            bool drawHoleCenters,
            bool drawArcCenters,
            bool drawBlindHoles,
            bool drawInnerProfiles,
            bool drawSideThickness,
            bool drawSideProjectionHoles,
            bool drawSideProjectionDepths,
            string tolBoundaryXminYmin,
            string tolBoundaryXminYmax,
            string tolBoundaryXmaxYmin,
            string tolBoundaryXmaxYmax,
            string tolOuterProfileVertices,
            string tolHoleCenters,
            string tolArcCenters,
            string tolBlindHoles,
            string tolInnerProfiles,
            string tolSideThickness,
            string tolSideProjectionHoles,
            string tolSideProjectionDepths,
            int projectionAngle,
            int sidePlacementModeIndex,
            double sideViewDistanceScale,
            double dimTextScale,
            int boundaryDimModeIndex,
            double dimOffsetScale,
            bool deleteDuplicateDim,
            short dimLineColorIndex,
            short dimTextColorIndex,
            bool createNotes,
            bool createNoteMarks,
            bool noteWireHole,
            bool noteThreadPitch,
            string noteTextStyleOption,
            string markTextStyleOption,
            double noteWidthFactor,
            double markTextScale,
            short noteTextColorIndex,
            short markColorIndex,
            string noteInsertPositionKey,
            double noteOffsetXScale,
            double noteOffsetYScale,
            int markPlacementModeIndex,
            double markStartAngleDeg,
            double markAngleStepDeg,
            int markRotateTries,
            double markDistanceScale,
            double markRingStepScale,
            double markMinDistanceScale,
            string sideOutlineLayerOption,
            string sideViewLayerOption,
            string noteLayerOption,
            string markLayerOption,
            string dimLayerOption)
        {
            Text = Lang.T("Form_Title");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Mode = new DimensionModeOptions();
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            int y = Margin;
            var processBox = new GroupBox { Text = Lang.T("Form_GroupProcessing") };
            processBox.SetBounds(Margin, y, FormWidth - Margin * 2, 220);
            Controls.Add(processBox);

            int processColWidth = (processBox.Width - RowLeft * 2 - ProcessingColumnGap - RightPad) / 2;
            int col1X = RowLeft;
            int col2X = RowLeft + processColWidth + ProcessingColumnGap;

            processBox.Controls.Add(MakeSectionLabel(Lang.T("Form_ProcessBoundary"), col1X, 22, processColWidth));
            _boundaryXminYmin = MakeCheck(Lang.T("Form_BoundaryXminYmin"));
            _boundaryXminYmax = MakeCheck(Lang.T("Form_BoundaryXminYmax"));
            _boundaryXmaxYmin = MakeCheck(Lang.T("Form_BoundaryXmaxYmin"));
            _boundaryXmaxYmax = MakeCheck(Lang.T("Form_BoundaryXmaxYmax"));
            _outerProfileVertices = MakeCheck(Lang.T("Form_OuterProfileVertices"));
            _sideProjectionHoles = MakeCheck(Lang.T("Form_SideProjectionHoles"));
            _tolBoundaryXminYmin = MakeToleranceBox();
            _tolBoundaryXminYmax = MakeToleranceBox();
            _tolBoundaryXmaxYmin = MakeToleranceBox();
            _tolBoundaryXmaxYmax = MakeToleranceBox();
            _tolOuterProfileVertices = MakeToleranceBox();
            _tolSideProjectionHoles = MakeToleranceBox();
            AddTolCheckAt(processBox, _boundaryXminYmin, _tolBoundaryXminYmin, col1X, 48, processColWidth);
            AddTolCheckAt(processBox, _boundaryXminYmax, _tolBoundaryXminYmax, col1X, 75, processColWidth);
            AddTolCheckAt(processBox, _boundaryXmaxYmin, _tolBoundaryXmaxYmin, col1X, 102, processColWidth);
            AddTolCheckAt(processBox, _boundaryXmaxYmax, _tolBoundaryXmaxYmax, col1X, 129, processColWidth);
            AddTolCheckAt(processBox, _outerProfileVertices, _tolOuterProfileVertices, col1X, 156, processColWidth);
            AddTolCheckAt(processBox, _sideProjectionHoles, _tolSideProjectionHoles, col1X, 183, processColWidth);

            processBox.Controls.Add(MakeSectionLabel(Lang.T("Form_ProcessInner"), col2X, 22, processColWidth));
            _holeCenters = MakeCheck(Lang.T("Form_HoleCenters"));
            _arcCenters = MakeCheck(Lang.T("Form_ArcCenters"));
            _blindHoles = MakeCheck(Lang.T("Form_BlindHoles"));
            _innerProfiles = MakeCheck(Lang.T("Form_InnerProfiles"));
            _sideThickness = MakeCheck(Lang.T("Form_SideThickness"));
            _sideProjectionDepths = MakeCheck(Lang.T("Form_SideProjectionDepths"));
            _tolHoleCenters = MakeToleranceBox();
            _tolArcCenters = MakeToleranceBox();
            _tolBlindHoles = MakeToleranceBox();
            _tolInnerProfiles = MakeToleranceBox();
            _tolSideThickness = MakeToleranceBox();
            _tolSideProjectionDepths = MakeToleranceBox();
            AddTolCheckAt(processBox, _holeCenters, _tolHoleCenters, col2X, 48, processColWidth);
            AddTolCheckAt(processBox, _arcCenters, _tolArcCenters, col2X, 75, processColWidth);
            AddTolCheckAt(processBox, _blindHoles, _tolBlindHoles, col2X, 102, processColWidth);
            AddTolCheckAt(processBox, _innerProfiles, _tolInnerProfiles, col2X, 129, processColWidth);
            AddTolCheckAt(processBox, _sideThickness, _tolSideThickness, col2X, 156, processColWidth);
            AddTolCheckAt(processBox, _sideProjectionDepths, _tolSideProjectionDepths, col2X, 183, processColWidth);

            y += processBox.Height + Gap;

            var settingsBox = new GroupBox { Text = Lang.T("Form_GroupSettings") };
            settingsBox.SetBounds(Margin, y, FormWidth - Margin * 2, 548);
            Controls.Add(settingsBox);

            int leftX = RowLeft;
            int rightX = RowLeft + SettingsColumnWidth + SettingsColumnGap;

            var sideBox = MakeInnerGroup(Lang.T("Form_GroupSide"), leftX, 24, SettingsColumnWidth, 132);
            settingsBox.Controls.Add(sideBox);

            _angleCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _angleCombo.Items.Add(Lang.T("Form_Custom"));
            _angleCombo.Items.Add("0°");
            _angleCombo.Items.Add("90°");
            _angleCombo.Items.Add("180°");
            _angleCombo.Items.Add("270°");
            _sidePlacementCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _sidePlacementCombo.Items.Add(Lang.T("Form_Auto"));
            _sidePlacementCombo.Items.Add(Lang.T("Form_Custom"));
            _sideViewDistanceScale = MakeDecimalBox(0.01m, 100.00m, 0.01m, 2);
            AddComboRow(sideBox, 0, Lang.T("Form_SelectAngle"), _angleCombo, SmallWidth);
            AddComboRow(sideBox, 1, Lang.T("Form_SidePlacement"), _sidePlacementCombo, SmallWidth);
            AddInputRow(sideBox, 2, Lang.T("Form_SideViewScale"), _sideViewDistanceScale, null);

            var dimBox = MakeInnerGroup(Lang.T("Form_GroupDim"), leftX, sideBox.Bottom + Gap, SettingsColumnWidth, 185);
            settingsBox.Controls.Add(dimBox);

            _boundaryDimModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _boundaryDimModeCombo.Items.Add(Lang.T("Form_DimOrdinate"));
            _boundaryDimModeCombo.Items.Add(Lang.T("Form_DimLinear"));
            _dimTextScale = MakeDecimalBox(0.01m, 10.00m, 0.01m, 2);
            _dimOffsetScale = MakeDecimalBox(0.01m, 100.00m, 0.01m, 2);
            _deleteDuplicateDim = MakeCheck(Lang.T("Form_DeleteDuplicateDim"));
            _dimLineColor = MakeIntegerBox(1, 255);
            _dimTextColor = MakeIntegerBox(1, 255);
            _dimLinePreview = new Panel { BorderStyle = BorderStyle.FixedSingle };
            _dimTextPreview = new Panel { BorderStyle = BorderStyle.FixedSingle };
            _dimLineColor.ValueChanged += delegate { UpdateColorPreview(); };
            _dimTextColor.ValueChanged += delegate { UpdateColorPreview(); };
            AddComboRow(dimBox, 0, Lang.T("Form_BoundaryDimMode"), _boundaryDimModeCombo, SmallWidth);
            AddInputRow(dimBox, 1, Lang.T("Form_DimTextScale"), _dimTextScale, null);
            AddInputRow(dimBox, 2, Lang.T("Form_DimOffsetScale"), _dimOffsetScale, null);
            AddCheckAt(dimBox, _deleteDuplicateDim, RowLeft, RowY(3), dimBox.Width - RowLeft - RightPad);
            AddInputRow(dimBox, 4, Lang.T("Form_LineColor"), _dimLineColor, _dimLinePreview);
            AddInputRow(dimBox, 5, Lang.T("Form_TextColor"), _dimTextColor, _dimTextPreview);

            var layerBox = MakeInnerGroup(Lang.T("Form_GroupLayer"), leftX, dimBox.Bottom + Gap, SettingsColumnWidth, 157);
            settingsBox.Controls.Add(layerBox);
            _sideOutlineLayerCombo = MakeLayerCombo();
            _sideViewLayerCombo = MakeLayerCombo();
            _noteLayerCombo = MakeLayerCombo();
            _markLayerCombo = MakeLayerCombo();
            _dimLayerCombo = MakeLayerCombo();
            AddComboRow(layerBox, 0, Lang.T("Form_LayerSideOutline"), _sideOutlineLayerCombo, SmallWidth);
            AddComboRow(layerBox, 1, Lang.T("Form_LayerSideView"), _sideViewLayerCombo, SmallWidth);
            AddComboRow(layerBox, 2, Lang.T("Form_LayerNotes"), _noteLayerCombo, SmallWidth);
            AddComboRow(layerBox, 3, Lang.T("Form_LayerMarks"), _markLayerCombo, SmallWidth);
            AddComboRow(layerBox, 4, Lang.T("Form_LayerDimensions"), _dimLayerCombo, SmallWidth);

            var notesBox = MakeInnerGroup(Lang.T("Form_GroupNotes"), rightX, 24, SettingsColumnWidth, 518);
            settingsBox.Controls.Add(notesBox);

            _createNotes = MakeCheck(Lang.T("Form_CreateNotes"));
            _createNotes.CheckedChanged += delegate { UpdateNoteControlState(); };
            _noteTextStyleCombo = MakeTextStyleCombo();
            _noteWidthFactor = MakeDecimalBox(0.01m, 5.00m, 0.01m, 2);
            _noteColor = MakeIntegerBox(1, 255);
            _noteColorPreview = new Panel { BorderStyle = BorderStyle.FixedSingle };
            _noteColor.ValueChanged += delegate { UpdateColorPreview(); };
            _noteInsertPosition = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _noteInsertPosition.Items.Add("Xmin,Ymin");
            _noteInsertPosition.Items.Add("Xmin,Ymax");
            _noteInsertPosition.Items.Add("Xmax,Ymin");
            _noteInsertPosition.Items.Add("Xmax,Ymax");
            _noteInsertPosition.Items.Add(Lang.T("Form_Custom"));
            _noteInsertPosition.SelectedIndexChanged += delegate { OnNoteInsertPositionChanged(); };
            _noteOffsetXScale = MakeDecimalBox(-100.00m, 100.00m, 0.01m, 2);
            _noteOffsetYScale = MakeDecimalBox(-100.00m, 100.00m, 0.01m, 2);

            _createMarks = MakeCheck(Lang.T("Form_CreateMarks"));
            _createMarks.CheckedChanged += delegate { UpdateNoteControlState(); };
            _markTextStyleCombo = MakeTextStyleCombo();
            _noteWireHole = MakeCheck(Lang.T("Form_NoteWireHole"));
            _noteThreadPitch = MakeCheck(Lang.T("Form_NoteThreadPitch"));
            _markTextScale = MakeDecimalBox(0.01m, 5.00m, 0.01m, 2);
            _markColor = MakeIntegerBox(1, 255);
            _markColorPreview = new Panel { BorderStyle = BorderStyle.FixedSingle };
            _markColor.ValueChanged += delegate { UpdateColorPreview(); };
            _markPositionCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _markPositionCombo.Items.Add(Lang.T("Form_MarkAuto"));
            _markPositionCombo.Items.Add(Lang.T("Form_MarkCenter"));
            _markStartAngleDeg = MakeDecimalBox(-360.00m, 360.00m, 1.00m, 0);
            _markAngleStepDeg = MakeDecimalBox(1.00m, 360.00m, 1.00m, 0);
            _markRotateTries = MakeIntegerBox(1, 72);
            _markDistanceScale = MakeDecimalBox(0.01m, 100.00m, 0.01m, 2);
            _markRingStepScale = MakeDecimalBox(0.01m, 100.00m, 0.01m, 2);
            _markMinDistanceScale = MakeDecimalBox(0.01m, 100.00m, 0.01m, 2);

            AddCheckComboAt(notesBox, _createNotes, Lang.T("Form_TextStyle"), _noteTextStyleCombo, RowLeft, RowTop, notesBox.Width - RowLeft - RightPad);
            AddInputRow(notesBox, 1, Lang.T("Form_NoteWidth"), _noteWidthFactor, null);
            AddInputRow(notesBox, 2, Lang.T("Form_TextColor"), _noteColor, _noteColorPreview);
            AddComboRow(notesBox, 3, Lang.T("Form_NotePosition"), _noteInsertPosition, SmallWidth);
            AddInputRow(notesBox, 4, Lang.T("Form_NoteOffsetX"), _noteOffsetXScale, null);
            AddInputRow(notesBox, 5, Lang.T("Form_NoteOffsetY"), _noteOffsetYScale, null);
            AddTwoChecksAt(notesBox, _noteWireHole, _noteThreadPitch, RowLeft, RowY(6), notesBox.Width - RowLeft - RightPad);
            notesBox.Controls.Add(MakeSectionLabel(Lang.T("Form_GroupMarkNotes"), RowLeft, RowY(7), notesBox.Width - RowLeft - RightPad));
            AddCheckComboAt(notesBox, _createMarks, Lang.T("Form_TextStyle"), _markTextStyleCombo, RowLeft, RowY(8), notesBox.Width - RowLeft - RightPad);
            AddInputRow(notesBox, 9, Lang.T("Form_MarkTextScale"), _markTextScale, null);
            AddInputRow(notesBox, 10, Lang.T("Form_TextColor"), _markColor, _markColorPreview);
            AddComboRow(notesBox, 11, Lang.T("Form_MarkPosition"), _markPositionCombo, SmallWidth);
            AddInputRow(notesBox, 12, Lang.T("Form_MarkStartAngle"), _markStartAngleDeg, null);
            AddInputRow(notesBox, 13, Lang.T("Form_MarkAngleStep"), _markAngleStepDeg, null);
            AddInputRow(notesBox, 14, Lang.T("Form_MarkRotateTries"), _markRotateTries, null);
            AddInputRow(notesBox, 15, Lang.T("Form_MarkDistance"), _markDistanceScale, null);
            AddInputRow(notesBox, 16, Lang.T("Form_MarkRingStep"), _markRingStepScale, null);
            AddInputRow(notesBox, 17, Lang.T("Form_MarkMinDistance"), _markMinDistanceScale, null);

            y += settingsBox.Height + Gap;

            var remark = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = Lang.T("Form_RemarkScale"),
                AutoEllipsis = true
            };
            remark.SetBounds(Margin, y, FormWidth - Margin * 2, 24);
            Controls.Add(remark);
            y += 32;

            var ok = new Button { Text = Lang.T("Btn_OK") };
            var cancel = new Button { Text = Lang.T("Btn_Cancel"), DialogResult = DialogResult.Cancel };
            var reset = new Button { Text = Lang.T("Btn_ResetDefault") };
            ok.SetBounds(FormWidth - Margin - 84, y, 84, 30);
            cancel.SetBounds(ok.Left - 8 - 84, y, 84, 30);
            reset.SetBounds(cancel.Left - 8 - 116, y, 116, 30);
            ok.Click += delegate { if (!ValidateLayerCompatibility()) return; ShowVietnameseNoteFontWarningIfNeeded(); CaptureValues(); DialogResult = DialogResult.OK; Close(); };
            reset.Click += delegate { ApplyResetDefaults(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            Controls.Add(reset);
            AcceptButton = ok;
            CancelButton = cancel;

            ClientSize = new Size(FormWidth, y + 30 + Margin);
            MinimumSize = new Size(FormWidth, y + 30 + Margin);

            ApplyValues(
                drawBoundaryXminYmin,
                drawBoundaryXminYmax,
                drawBoundaryXmaxYmin,
                drawBoundaryXmaxYmax,
                drawOuterProfileVertices,
                drawHoleCenters,
                drawArcCenters,
                drawBlindHoles,
                drawInnerProfiles,
                drawSideThickness,
                drawSideProjectionHoles,
                drawSideProjectionDepths,
                tolBoundaryXminYmin,
                tolBoundaryXminYmax,
                tolBoundaryXmaxYmin,
                tolBoundaryXmaxYmax,
                tolOuterProfileVertices,
                tolHoleCenters,
                tolArcCenters,
                tolBlindHoles,
                tolInnerProfiles,
                tolSideThickness,
                tolSideProjectionHoles,
                tolSideProjectionDepths,
                projectionAngle,
                sidePlacementModeIndex,
                sideViewDistanceScale,
                dimTextScale,
                boundaryDimModeIndex,
                dimOffsetScale,
                deleteDuplicateDim,
                dimLineColorIndex,
                dimTextColorIndex,
                createNotes,
                createNoteMarks,
                noteWireHole,
                noteThreadPitch,
                noteTextStyleOption,
                markTextStyleOption,
                noteWidthFactor,
                markTextScale,
                noteTextColorIndex,
                markColorIndex,
                noteInsertPositionKey,
                noteOffsetXScale,
                noteOffsetYScale,
                markPlacementModeIndex,
                markStartAngleDeg,
                markAngleStepDeg,
                markRotateTries,
                markDistanceScale,
                markRingStepScale,
                markMinDistanceScale,
                sideOutlineLayerOption,
                sideViewLayerOption,
                noteLayerOption,
                markLayerOption,
                dimLayerOption);
        }

        private static GroupBox MakeInnerGroup(string title, int x, int y, int w, int h)
        {
            var box = new GroupBox { Text = title };
            box.SetBounds(x, y, w, h);
            return box;
        }

        private static Label MakeSectionLabel(string text, int x, int y, int w)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Bounds = new Rectangle(x, y, w, SectionHeaderHeight)
            };
        }

        private static CheckBox MakeCheck(string text)
        {
            return new CheckBox { Text = text, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static ComboBox MakeLayerCombo()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                DisplayMember = "DisplayName",
                ValueMember = "Value",
                IntegralHeight = true
            };
            combo.Items.AddRange(LayerCatalog.Items().ToArray());
            combo.DropDownWidth = Math.Max(SmallWidth, 230);
            return combo;
        }

        private static ComboBox MakeTextStyleCombo()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                DisplayMember = "DisplayName",
                ValueMember = "Value",
                IntegralHeight = true
            };
            combo.Items.Add(new TextStyleOption("Current", Lang.T("Form_TextStyleCurrent")));
            combo.Items.Add(new TextStyleOption("Isocp", "Isocp"));
            combo.Items.Add(new TextStyleOption("txt", "txt"));
            combo.Items.Add(new TextStyleOption("Arial", "Arial"));
            combo.DropDownWidth = Math.Max(SmallWidth, 180);
            return combo;
        }

        private static string DefaultNoteTextStyleOption()
        {
            return IsVietnameseLanguage() ? "Arial" : "Isocp";
        }

        private static string DefaultMarkTextStyleOption()
        {
            return "Isocp";
        }

        private static bool IsVietnameseLanguage()
        {
            if (Lang.CurrentLangId == 1) return true;
            string name = Lang.CurrentLanguageName ?? string.Empty;
            return name.IndexOf("Vietnamese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Viet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeTextStyleOption(string value, string fallback)
        {
            string s = (value ?? string.Empty).Trim();
            if (s.Length == 0 || string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase))
                return fallback;
            if (string.Equals(s, "Current", StringComparison.OrdinalIgnoreCase)) return "Current";
            if (string.Equals(s, "Isocp", StringComparison.OrdinalIgnoreCase)) return "Isocp";
            if (string.Equals(s, "txt", StringComparison.OrdinalIgnoreCase)) return "txt";
            if (string.Equals(s, "Arial", StringComparison.OrdinalIgnoreCase)) return "Arial";
            return fallback;
        }

        private static void SelectTextStyleOption(ComboBox combo, string value, string fallback)
        {
            if (combo == null) return;
            string wanted = NormalizeTextStyleOption(value, fallback);
            for (int i = 0; i < combo.Items.Count; i++)
            {
                TextStyleOption opt = combo.Items[i] as TextStyleOption;
                if (opt != null && string.Equals(opt.Value, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string GetTextStyleOption(ComboBox combo, string fallback)
        {
            TextStyleOption opt = combo == null ? null : combo.SelectedItem as TextStyleOption;
            return opt == null ? fallback : NormalizeTextStyleOption(opt.Value, fallback);
        }

        private static void SelectLayerOption(ComboBox combo, string value)
        {
            if (combo == null) return;
            string wanted = LayerCatalog.Find(value).Value;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                LayerOption opt = combo.Items[i] as LayerOption;
                if (opt != null && string.Equals(opt.Value, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string GetLayerOption(ComboBox combo)
        {
            LayerOption opt = combo == null ? null : combo.SelectedItem as LayerOption;
            return opt == null ? LayerCatalog.DefaultValue : opt.Value;
        }

        private static void AddCheckAt(Control parent, CheckBox check, int x, int y, int w)
        {
            check.SetBounds(x, y, w, RowHeight);
            parent.Controls.Add(check);
        }

        private static void AddTwoChecksAt(Control parent, CheckBox first, CheckBox second, int x, int y, int w)
        {
            int gap = 12;
            int firstW = Math.Max(120, (w - gap) / 2);
            int secondW = Math.Max(100, w - firstW - gap);
            first.SetBounds(x, y, firstW, RowHeight);
            second.SetBounds(x + firstW + gap, y, secondW, RowHeight);
            parent.Controls.Add(first);
            parent.Controls.Add(second);
        }

        private static void AddCheckComboAt(Control parent, CheckBox check, string comboLabel, ComboBox combo, int x, int y, int w)
        {
            int comboW = SmallWidth;
            int comboX = x + w - comboW;
            int labelW = 62;
            int labelX = comboX - LabelGap - labelW;
            int checkW = Math.Max(80, labelX - x - LabelGap);
            check.SetBounds(x, y, checkW, RowHeight);
            parent.Controls.Add(check);
            parent.Controls.Add(MakeLabel(comboLabel, labelX, y, labelW, RowHeight));
            combo.SetBounds(comboX, y, comboW, RowHeight);
            parent.Controls.Add(combo);
        }

        private static TextBox MakeToleranceBox()
        {
            return new TextBox { Width = TolWidth, Height = RowHeight, TextAlign = HorizontalAlignment.Right };
        }

        private static void AddTolCheckAt(Control parent, CheckBox check, TextBox tol, int x, int y, int w)
        {
            // Layout requested V64: checkbox -> tolerance box -> explanatory text.
            string labelText = check.Text;
            check.Text = string.Empty;
            check.SetBounds(x, y, 22, RowHeight);

            int tolX = x + 26;
            tol.SetBounds(tolX, y + 1, TolWidth, RowHeight);

            int labelX = tolX + TolWidth + 6;
            int labelW = Math.Max(40, w - (labelX - x));
            Label label = MakeLabel(labelText, labelX, y, labelW, RowHeight);

            parent.Controls.Add(check);
            parent.Controls.Add(tol);
            parent.Controls.Add(label);
        }

        private static Label MakeLabel(string text, int x, int y, int w, int h)
        {
            var label = new Label { Text = text, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            label.SetBounds(x, y, w, h);
            return label;
        }

        private static int RowY(int rowIndex)
        {
            return RowTop + rowIndex * RowPitch;
        }

        private static NumericUpDown MakeDecimalBox(decimal min, decimal max, decimal inc, int decimals)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = decimals, TextAlign = HorizontalAlignment.Right, Width = SmallWidth, Height = RowHeight };
        }

        private static NumericUpDown MakeIntegerBox(int min, int max)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, DecimalPlaces = 0, TextAlign = HorizontalAlignment.Right, Width = SmallWidth, Height = RowHeight };
        }

        private static void AddComboRow(Control parent, int rowIndex, string label, ComboBox combo, int comboWidth)
        {
            int y = RowY(rowIndex);
            int inputX = parent.Width - RightPad - comboWidth;
            int labelW = inputX - RowLeft - LabelGap;
            parent.Controls.Add(MakeLabel(label, RowLeft, y, labelW, RowHeight));
            combo.SetBounds(inputX, y, comboWidth, RowHeight);
            parent.Controls.Add(combo);
        }

        private static void AddInputRow(Control parent, int rowIndex, string label, Control input, Control preview)
        {
            int y = RowY(rowIndex);
            int inputX = parent.Width - RightPad - SmallWidth;
            int previewX = inputX;
            int labelRight = inputX - LabelGap;

            if (preview != null)
            {
                previewX = inputX - ColorWidth - 6;
                labelRight = previewX - LabelGap;
                preview.SetBounds(previewX, y + 1, ColorWidth, RowHeight - 2);
                parent.Controls.Add(preview);
            }

            int labelW = Math.Max(40, labelRight - RowLeft);
            parent.Controls.Add(MakeLabel(label, RowLeft, y, labelW, RowHeight));
            input.SetBounds(inputX, y, SmallWidth, RowHeight);
            parent.Controls.Add(input);
        }

        private void ApplyResetDefaults()
        {
            ApplyValues(
                true,
                false,
                false,
                true,
                false,
                true,
                true,
                false,
                false,
                true,
                false,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                270,
                0,
                8.0,
                1.0,
                0,
                3.0,
                true,
                3,
                123,
                true,
                true,
                false,
                false,
                DefaultNoteTextStyleOption(),
                DefaultMarkTextStyleOption(),
                0.88,
                0.80,
                123,
                123,
                AnchorXmaxYmax,
                4.0,
                0.0,
                0,
                45.0,
                45.0,
                8,
                0.60,
                0.80,
                1.50,
                LayerCatalog.SourceObjectValue,
                LayerCatalog.DefaultValue,
                LayerCatalog.Find("t|TEXT").Value,
                LayerCatalog.Find("t|TEXT").Value,
                LayerCatalog.Find("d|DIM").Value);
        }

        private void ApplyValues(
            bool drawBoundaryXminYmin,
            bool drawBoundaryXminYmax,
            bool drawBoundaryXmaxYmin,
            bool drawBoundaryXmaxYmax,
            bool drawOuterProfileVertices,
            bool drawHoleCenters,
            bool drawArcCenters,
            bool drawBlindHoles,
            bool drawInnerProfiles,
            bool drawSideThickness,
            bool drawSideProjectionHoles,
            bool drawSideProjectionDepths,
            string tolBoundaryXminYmin,
            string tolBoundaryXminYmax,
            string tolBoundaryXmaxYmin,
            string tolBoundaryXmaxYmax,
            string tolOuterProfileVertices,
            string tolHoleCenters,
            string tolArcCenters,
            string tolBlindHoles,
            string tolInnerProfiles,
            string tolSideThickness,
            string tolSideProjectionHoles,
            string tolSideProjectionDepths,
            int projectionAngle,
            int sidePlacementModeIndex,
            double sideViewDistanceScale,
            double dimTextScale,
            int boundaryDimModeIndex,
            double dimOffsetScale,
            bool deleteDuplicateDim,
            short dimLineColorIndex,
            short dimTextColorIndex,
            bool createNotes,
            bool createNoteMarks,
            bool noteWireHole,
            bool noteThreadPitch,
            string noteTextStyleOption,
            string markTextStyleOption,
            double noteWidthFactor,
            double markTextScale,
            short noteTextColorIndex,
            short markColorIndex,
            string noteInsertPositionKey,
            double noteOffsetXScale,
            double noteOffsetYScale,
            int markPlacementModeIndex,
            double markStartAngleDeg,
            double markAngleStepDeg,
            int markRotateTries,
            double markDistanceScale,
            double markRingStepScale,
            double markMinDistanceScale,
            string sideOutlineLayerOption,
            string sideViewLayerOption,
            string noteLayerOption,
            string markLayerOption,
            string dimLayerOption)
        {
            _loading = true;
            _boundaryXminYmin.Checked = drawBoundaryXminYmin;
            _boundaryXminYmax.Checked = drawBoundaryXminYmax;
            _boundaryXmaxYmin.Checked = drawBoundaryXmaxYmin;
            _boundaryXmaxYmax.Checked = drawBoundaryXmaxYmax;
            _outerProfileVertices.Checked = drawOuterProfileVertices;
            _holeCenters.Checked = drawHoleCenters;
            _arcCenters.Checked = drawArcCenters;
            _blindHoles.Checked = drawBlindHoles;
            _innerProfiles.Checked = drawInnerProfiles;
            _sideThickness.Checked = drawSideThickness;
            _sideProjectionHoles.Checked = drawSideProjectionHoles;
            _sideProjectionDepths.Checked = drawSideProjectionDepths;
            _tolBoundaryXminYmin.Text = tolBoundaryXminYmin ?? string.Empty;
            _tolBoundaryXminYmax.Text = tolBoundaryXminYmax ?? string.Empty;
            _tolBoundaryXmaxYmin.Text = tolBoundaryXmaxYmin ?? string.Empty;
            _tolBoundaryXmaxYmax.Text = tolBoundaryXmaxYmax ?? string.Empty;
            _tolOuterProfileVertices.Text = tolOuterProfileVertices ?? string.Empty;
            _tolHoleCenters.Text = tolHoleCenters ?? string.Empty;
            _tolArcCenters.Text = tolArcCenters ?? string.Empty;
            _tolBlindHoles.Text = tolBlindHoles ?? string.Empty;
            _tolInnerProfiles.Text = tolInnerProfiles ?? string.Empty;
            _tolSideThickness.Text = tolSideThickness ?? string.Empty;
            _tolSideProjectionHoles.Text = tolSideProjectionHoles ?? string.Empty;
            _tolSideProjectionDepths.Text = tolSideProjectionDepths ?? string.Empty;
            SelectAngle(projectionAngle);
            _sidePlacementCombo.SelectedIndex = ClampInt(sidePlacementModeIndex, 0, 1, 0);
            _sideViewDistanceScale.Value = ToDecimal(NormalizeDouble(sideViewDistanceScale, 8.0, 0.01, 100.0));
            _dimTextScale.Value = ToDecimal(NormalizeDouble(dimTextScale, 1.0, 0.01, 10.0));
            _boundaryDimModeCombo.SelectedIndex = ClampInt(boundaryDimModeIndex, 0, 1, 0);
            _dimOffsetScale.Value = ToDecimal(NormalizeDouble(dimOffsetScale, 3.0, 0.01, 100.0));
            _deleteDuplicateDim.Checked = deleteDuplicateDim;
            _dimLineColor.Value = NormalizeColor(dimLineColorIndex, 3);
            _dimTextColor.Value = NormalizeColor(dimTextColorIndex, 123);
            _createNotes.Checked = createNotes;
            _createMarks.Checked = createNoteMarks;
            _noteWireHole.Checked = noteWireHole;
            _noteThreadPitch.Checked = noteThreadPitch;
            SelectTextStyleOption(_noteTextStyleCombo, noteTextStyleOption, DefaultNoteTextStyleOption());
            SelectTextStyleOption(_markTextStyleCombo, markTextStyleOption, DefaultMarkTextStyleOption());
            _noteWidthFactor.Value = ToDecimal(NormalizeDouble(noteWidthFactor, 0.88, 0.01, 5.0));
            _markTextScale.Value = ToDecimal(NormalizeDouble(markTextScale, 0.80, 0.01, 5.0));
            _noteColor.Value = NormalizeColor(noteTextColorIndex, 123);
            _markColor.Value = NormalizeColor(markColorIndex, 123);
            SelectNoteInsertPosition(noteInsertPositionKey);
            _noteOffsetXScale.Value = ToDecimal(NormalizeDouble(noteOffsetXScale, DefaultNoteOffsetXForAnchor(NoteInsertPositionKeyFromCombo()), -100.0, 100.0));
            _noteOffsetYScale.Value = ToDecimal(NormalizeDouble(noteOffsetYScale, DefaultNoteOffsetYForAnchor(NoteInsertPositionKeyFromCombo()), -100.0, 100.0));
            _markPositionCombo.SelectedIndex = ClampInt(markPlacementModeIndex, 0, 1, 0);
            _markStartAngleDeg.Value = ToDecimal(NormalizeDouble(markStartAngleDeg, 45.0, -360.0, 360.0));
            _markAngleStepDeg.Value = ToDecimal(NormalizeDouble(markAngleStepDeg, 45.0, 1.0, 360.0));
            _markRotateTries.Value = ClampInt(markRotateTries, 1, 72, 8);
            _markDistanceScale.Value = ToDecimal(NormalizeDouble(markDistanceScale, 0.60, 0.01, 100.0));
            _markRingStepScale.Value = ToDecimal(NormalizeDouble(markRingStepScale, 0.80, 0.01, 100.0));
            _markMinDistanceScale.Value = ToDecimal(NormalizeDouble(markMinDistanceScale, 1.50, 0.01, 100.0));
            SelectLayerOption(_sideOutlineLayerCombo, sideOutlineLayerOption);
            SelectLayerOption(_sideViewLayerCombo, sideViewLayerOption);
            SelectLayerOption(_noteLayerCombo, noteLayerOption);
            SelectLayerOption(_markLayerCombo, markLayerOption);
            SelectLayerOption(_dimLayerCombo, dimLayerOption);
            _loading = false;
            UpdateNoteControlState();
            UpdateColorPreview();
        }

        private void SelectAngle(int angle)
        {
            if (angle == 0) _angleCombo.SelectedIndex = 1;
            else if (angle == 90) _angleCombo.SelectedIndex = 2;
            else if (angle == 180) _angleCombo.SelectedIndex = 3;
            else _angleCombo.SelectedIndex = 4;
        }

        private void SelectNoteInsertPosition(string key)
        {
            if (string.Equals(key, AnchorXminYmin, StringComparison.OrdinalIgnoreCase)) _noteInsertPosition.SelectedIndex = 0;
            else if (string.Equals(key, AnchorXminYmax, StringComparison.OrdinalIgnoreCase)) _noteInsertPosition.SelectedIndex = 1;
            else if (string.Equals(key, AnchorXmaxYmin, StringComparison.OrdinalIgnoreCase)) _noteInsertPosition.SelectedIndex = 2;
            else if (string.Equals(key, AnchorPickPoint, StringComparison.OrdinalIgnoreCase)) _noteInsertPosition.SelectedIndex = 4;
            else _noteInsertPosition.SelectedIndex = 3;
        }

        private string NoteInsertPositionKeyFromCombo()
        {
            switch (_noteInsertPosition.SelectedIndex)
            {
                case 0: return AnchorXminYmin;
                case 1: return AnchorXminYmax;
                case 2: return AnchorXmaxYmin;
                case 4: return AnchorPickPoint;
                default: return AnchorXmaxYmax;
            }
        }

        private void OnNoteInsertPositionChanged()
        {
            if (_loading) return;
            string key = NoteInsertPositionKeyFromCombo();
            _noteOffsetXScale.Value = ToDecimal(DefaultNoteOffsetXForAnchor(key));
            _noteOffsetYScale.Value = ToDecimal(DefaultNoteOffsetYForAnchor(key));
        }

        private void ShowVietnameseNoteFontWarningIfNeeded()
        {
            if (!IsVietnameseLanguage()) return;
            if (_createNotes == null || !_createNotes.Checked) return;
            string style = GetTextStyleOption(_noteTextStyleCombo, DefaultNoteTextStyleOption());
            if (string.Equals(style, "Arial", StringComparison.OrdinalIgnoreCase)) return;
            MessageBox.Show(this, Lang.T("Msg_VietnameseFontWarning"), Lang.T("Form_Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private bool ValidateLayerCompatibility()
        {
            if (LayerCatalog.IsSourceObjectOption(GetLayerOption(_noteLayerCombo)))
                return ShowLayerIncompatibleMessage(Lang.T("Form_LayerNotes"));
            if (LayerCatalog.IsSourceObjectOption(GetLayerOption(_markLayerCombo)))
                return ShowLayerIncompatibleMessage(Lang.T("Form_LayerMarks"));
            if (LayerCatalog.IsSourceObjectOption(GetLayerOption(_dimLayerCombo)))
                return ShowLayerIncompatibleMessage(Lang.T("Form_LayerDimensions"));
            return true;
        }

        private bool ShowLayerIncompatibleMessage(string rowName)
        {
            string message = Lang.F("Msg_LayerSourceNotCompatible", rowName);
            if (string.IsNullOrWhiteSpace(message) || message.IndexOf("Msg_LayerSourceNotCompatible", StringComparison.OrdinalIgnoreCase) >= 0)
                message = rowName + ": Layer của đối tượng gốc không tương thích với mục này.";
            MessageBox.Show(this, message, Lang.T("Form_Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void CaptureValues()
        {
            Mode.BoundaryXminYminDims = _boundaryXminYmin.Checked;
            Mode.BoundaryXminYmaxDims = _boundaryXminYmax.Checked;
            Mode.BoundaryXmaxYminDims = _boundaryXmaxYmin.Checked;
            Mode.BoundaryXmaxYmaxDims = _boundaryXmaxYmax.Checked;
            Mode.OuterProfileVertexDims = _outerProfileVertices.Checked;
            Mode.HoleCenterDims = _holeCenters.Checked;
            Mode.ArcCenterDims = _arcCenters.Checked;
            Mode.DepthHoleDims = _blindHoles.Checked;
            Mode.InnerProfileDims = _innerProfiles.Checked;
            Mode.SideThicknessDims = _sideThickness.Checked;
            Mode.SideProjectionHoleDims = _sideProjectionHoles.Checked;
            Mode.SideProjectionDepthDims = _sideProjectionDepths.Checked;
            TolBoundaryXminYmin = SanitizeToleranceText(_tolBoundaryXminYmin.Text);
            TolBoundaryXminYmax = SanitizeToleranceText(_tolBoundaryXminYmax.Text);
            TolBoundaryXmaxYmin = SanitizeToleranceText(_tolBoundaryXmaxYmin.Text);
            TolBoundaryXmaxYmax = SanitizeToleranceText(_tolBoundaryXmaxYmax.Text);
            TolOuterProfileVertices = SanitizeToleranceText(_tolOuterProfileVertices.Text);
            TolHoleCenters = SanitizeToleranceText(_tolHoleCenters.Text);
            TolArcCenters = SanitizeToleranceText(_tolArcCenters.Text);
            TolBlindHoles = SanitizeToleranceText(_tolBlindHoles.Text);
            TolInnerProfiles = SanitizeToleranceText(_tolInnerProfiles.Text);
            TolSideThickness = SanitizeToleranceText(_tolSideThickness.Text);
            TolSideProjectionHoles = SanitizeToleranceText(_tolSideProjectionHoles.Text);
            TolSideProjectionDepths = SanitizeToleranceText(_tolSideProjectionDepths.Text);
            RequestCustomProjectionAngle = _angleCombo.SelectedIndex == 0;
            ProjectionAngle = _angleCombo.SelectedIndex == 1 ? 0 : (_angleCombo.SelectedIndex == 2 ? 90 : (_angleCombo.SelectedIndex == 3 ? 180 : 270));
            SidePlacementModeIndex = _sidePlacementCombo.SelectedIndex == 1 ? 1 : 0;
            SideViewDistanceScale = (double)_sideViewDistanceScale.Value;
            BoundaryDimModeIndex = _boundaryDimModeCombo.SelectedIndex == 1 ? 1 : 0;
            DimTextScale = (double)_dimTextScale.Value;
            DimOffsetScale = (double)_dimOffsetScale.Value;
            DeleteDuplicateDim = _deleteDuplicateDim.Checked;
            DimLineColorIndex = (short)_dimLineColor.Value;
            DimTextColorIndex = (short)_dimTextColor.Value;
            CreateNotes = _createNotes.Checked;
            CreateNoteMarks = _createNotes.Checked && _createMarks.Checked;
            NoteWireHole = _noteWireHole.Checked;
            NoteThreadPitch = _noteThreadPitch.Checked;
            NoteTextStyleOption = GetTextStyleOption(_noteTextStyleCombo, DefaultNoteTextStyleOption());
            MarkTextStyleOption = GetTextStyleOption(_markTextStyleCombo, DefaultMarkTextStyleOption());
            NoteWidthFactor = (double)_noteWidthFactor.Value;
            MarkTextScale = (double)_markTextScale.Value;
            NoteTextColorIndex = (short)_noteColor.Value;
            MarkColorIndex = (short)_markColor.Value;
            NoteInsertPositionKey = NoteInsertPositionKeyFromCombo();
            NoteOffsetXScale = (double)_noteOffsetXScale.Value;
            NoteOffsetYScale = (double)_noteOffsetYScale.Value;
            MarkPlacementModeIndex = _markPositionCombo.SelectedIndex == 1 ? 1 : 0;
            MarkStartAngleDeg = (double)_markStartAngleDeg.Value;
            MarkAngleStepDeg = (double)_markAngleStepDeg.Value;
            MarkRotateTries = (int)_markRotateTries.Value;
            MarkDistanceScale = (double)_markDistanceScale.Value;
            MarkRingStepScale = (double)_markRingStepScale.Value;
            MarkMinDistanceScale = (double)_markMinDistanceScale.Value;
            SideOutlineLayerOption = GetLayerOption(_sideOutlineLayerCombo);
            SideViewLayerOption = GetLayerOption(_sideViewLayerCombo);
            NoteLayerOption = GetLayerOption(_noteLayerCombo);
            MarkLayerOption = GetLayerOption(_markLayerCombo);
            DimLayerOption = GetLayerOption(_dimLayerCombo);
        }

        private void UpdateNoteControlState()
        {
            if (_createNotes == null || _createMarks == null || _noteWireHole == null || _noteThreadPitch == null) return;
            bool enabled = _createNotes.Checked;
            bool markEnabled = enabled && _createMarks.Checked;
            _createMarks.Enabled = enabled;
            _noteWidthFactor.Enabled = enabled;
            _noteColor.Enabled = enabled;
            _noteColorPreview.Enabled = enabled;
            _noteInsertPosition.Enabled = enabled;
            _noteOffsetXScale.Enabled = enabled;
            _noteOffsetYScale.Enabled = enabled;
            _noteWireHole.Enabled = enabled;
            _noteThreadPitch.Enabled = enabled;
            _noteTextStyleCombo.Enabled = enabled;
            _markTextStyleCombo.Enabled = markEnabled;
            _markTextScale.Enabled = markEnabled;
            _markColor.Enabled = markEnabled;
            _markColorPreview.Enabled = markEnabled;
            _markPositionCombo.Enabled = markEnabled;
            _markStartAngleDeg.Enabled = markEnabled;
            _markAngleStepDeg.Enabled = markEnabled;
            _markRotateTries.Enabled = markEnabled;
            _markDistanceScale.Enabled = markEnabled;
            _markRingStepScale.Enabled = markEnabled;
            _markMinDistanceScale.Enabled = markEnabled;
            if (!enabled) _createMarks.Checked = false;
        }

        private void UpdateColorPreview()
        {
            if (_dimLinePreview != null) _dimLinePreview.BackColor = AciApproxColor((int)_dimLineColor.Value);
            if (_dimTextPreview != null) _dimTextPreview.BackColor = AciApproxColor((int)_dimTextColor.Value);
            if (_noteColorPreview != null) _noteColorPreview.BackColor = AciApproxColor((int)_noteColor.Value);
            if (_markColorPreview != null) _markColorPreview.BackColor = AciApproxColor((int)_markColor.Value);
        }

        private static double DefaultNoteOffsetXForAnchor(string key)
        {
            if (string.Equals(key, AnchorXmaxYmax, StringComparison.OrdinalIgnoreCase)) return 4.0;
            if (string.Equals(key, AnchorPickPoint, StringComparison.OrdinalIgnoreCase)) return 0.0;
            return 0.0;
        }

        private static double DefaultNoteOffsetYForAnchor(string key)
        {
            if (string.Equals(key, AnchorXminYmin, StringComparison.OrdinalIgnoreCase)) return 4.0;
            return 0.0;
        }

        private static string SanitizeToleranceText(string value)
        {
            string s = (value ?? string.Empty).Trim().Replace(',', '.');
            return s;
        }

        private static double NormalizeDouble(double value, double fallback, double min, double max)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max) return value;
            return fallback;
        }

        private static short NormalizeColor(short value, short fallback)
        {
            if (value >= 1 && value <= 255) return value;
            if (fallback >= 1 && fallback <= 255) return fallback;
            return 123;
        }

        private static int ClampInt(int value, int min, int max, int fallback)
        {
            if (value < min || value > max) return fallback;
            return value;
        }

        private static decimal ToDecimal(double value)
        {
            try { return Convert.ToDecimal(value); }
            catch { return 1m; }
        }

        private static Color AciApproxColor(int index)
        {
            switch (index)
            {
                case 1: return Color.Red;
                case 2: return Color.Yellow;
                case 3: return Color.Lime;
                case 4: return Color.Cyan;
                case 5: return Color.Blue;
                case 6: return Color.Magenta;
                case 7: return Color.White;
                case 8: return Color.Gray;
                case 9: return Color.LightGray;
                default:
                    int r = (index * 53) % 256;
                    int g = (index * 97) % 256;
                    int b = (index * 193) % 256;
                    return Color.FromArgb(r, g, b);
            }
        }
    }
}
