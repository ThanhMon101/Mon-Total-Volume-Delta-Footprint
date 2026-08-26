using System;
using System.IO;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace ATAS.Indicators.Custom
{
    [Category("Custom")]
    [DisplayName("Mon - Total Volume & Delta Footprint")]
    [Description("Displays custom footprint candles with Total Volume on the left and Delta on the right, featuring volume highlights, middle candles, Ticks Grouping, bottom stats, right-side volume profile, stacked imbalances, and Delta Divergence arrows.")]
    public class TotalVolDeltaFootprint : Indicator
    {
        protected virtual void OnPropertyChanged(string propertyName) => RaisePropertyChanged(propertyName);

        // ----------------------------------------------------
        // Profile Enum
        // ----------------------------------------------------
        public enum IndicatorProfile
        {
            Default,
            Profile1,
            Profile2,
            Profile3,
            Profile4,
            Profile5
        }

        // A dedicated four-value enum makes ATAS render Quick Setup as a
        // native, non-editable dropdown. IndicatorProfile remains unchanged
        // internally so existing profile files and saved charts stay compatible.
        public enum QuickTradingProfile
        {
            [Display(Name = "NQ | RTH | 09:30-16:00 ET")]
            NQ_RTH,

            [Display(Name = "NQ | ETH | 18:00-09:30 ET")]
            NQ_ETH,

            [Display(Name = "ES | RTH | 09:30-16:00 ET")]
            ES_RTH,

            [Display(Name = "ES | ETH | 18:00-09:30 ET")]
            ES_ETH
        }

        // ----------------------------------------------------
        // Color Theme Enum (Dark Mode / Light Mode / Custom)
        // ----------------------------------------------------
        public enum ColorThemeMode
        {
            DarkMode,
            LightMode,
            Custom
        }

        private const string QuickSetupGroup = "01. QUICK SETUP";

        private static string GetDefaultProfileLabel(IndicatorProfile profile)
        {
            return profile switch
            {
                IndicatorProfile.Default => "NQ | RTH | 09:30-16:00 ET",
                IndicatorProfile.Profile1 => "NQ | ETH | 18:00-09:30 ET",
                IndicatorProfile.Profile2 => "ES | RTH | 09:30-16:00 ET",
                IndicatorProfile.Profile3 => "ES | ETH | 18:00-09:30 ET",
                IndicatorProfile.Profile4 => "Custom 1",
                IndicatorProfile.Profile5 => "Custom 2",
                _ => profile.ToString()
            };
        }

        private static string GetProfileScope(IndicatorProfile profile)
        {
            return profile switch
            {
                IndicatorProfile.Default => "NQ | Regular session | 09:30-16:00 US Eastern",
                IndicatorProfile.Profile1 => "NQ | ETH | 18:00-09:30 US Eastern",
                IndicatorProfile.Profile2 => "ES | Regular session | 09:30-16:00 US Eastern",
                IndicatorProfile.Profile3 => "ES | ETH | 18:00-09:30 US Eastern",
                _ => "User-defined profile"
            };
        }

        private static bool IsBuiltInTradingProfile(IndicatorProfile profile)
            => profile >= IndicatorProfile.Default && profile <= IndicatorProfile.Profile3;

        private static IndicatorProfile ToIndicatorProfile(QuickTradingProfile profile)
        {
            return profile switch
            {
                QuickTradingProfile.NQ_RTH => IndicatorProfile.Default,
                QuickTradingProfile.NQ_ETH => IndicatorProfile.Profile1,
                QuickTradingProfile.ES_RTH => IndicatorProfile.Profile2,
                QuickTradingProfile.ES_ETH => IndicatorProfile.Profile3,
                _ => IndicatorProfile.Default
            };
        }

        private static QuickTradingProfile ToQuickTradingProfile(IndicatorProfile profile)
        {
            return profile switch
            {
                IndicatorProfile.Profile1 => QuickTradingProfile.NQ_ETH,
                IndicatorProfile.Profile2 => QuickTradingProfile.ES_RTH,
                IndicatorProfile.Profile3 => QuickTradingProfile.ES_ETH,
                _ => QuickTradingProfile.NQ_RTH
            };
        }

        private static string GetCalibrationStatus(IndicatorProfile profile)
        {
            return profile switch
            {
                IndicatorProfile.Default => "USER-TUNED BASELINE | Primary preset",
                IndicatorProfile.Profile1 => "RECOMMENDED BASELINE | Paper-test ETH first",
                IndicatorProfile.Profile2 => "RECOMMENDED BASELINE | Backtest ES first",
                IndicatorProfile.Profile3 => "RECOMMENDED BASELINE | Paper-test ETH first",
                _ => "USER-DEFINED"
            };
        }

        private static (int Hour, int Minute) GetDefaultSessionReset(IndicatorProfile profile)
        {
            return profile switch
            {
                IndicatorProfile.Default or IndicatorProfile.Profile2 => (9, 30),
                IndicatorProfile.Profile1 or IndicatorProfile.Profile3 => (18, 0),
                _ => (0, 0)
            };
        }

        // ----------------------------------------------------
        // Grouped Price Level Helper Class
        // ----------------------------------------------------
        public class GroupedPriceLevel
        {
            public decimal Price { get; set; }
            public decimal Volume { get; set; }
            public decimal Ask { get; set; }
            public decimal Bid { get; set; }
            public decimal Delta => Ask - Bid;
        }

        // ----------------------------------------------------
        // Stacked Imbalance Line Class
        // ----------------------------------------------------
        public class ImbalanceLine
        {
            public decimal Price { get; set; }
            public int StartBar { get; set; }
            public bool IsBuy { get; set; } // true = Ask/Bid (Buy), false = Bid/Ask (Sell)
            public bool IsMitigated { get; set; }
            public int MitigatedBar { get; set; }
        }

        // ----------------------------------------------------
        // Delta Divergence Point Class (with Invalidation State)
        // ----------------------------------------------------
        public class DivergencePoint
        {
            public int Bar { get; set; }
            public bool IsBullish { get; set; }
            public decimal Price { get; set; } // Low for bullish, High for bearish
            public bool IsMajor { get; set; } // true = Major, false = Minor
            public bool IsInvalidated { get; set; }
            public int InvalidatedBar { get; set; }
        }

        // ----------------------------------------------------
        // Private Fields for Reused Rendering Objects (Performance Optimization)
        // ----------------------------------------------------
        private RenderFont? _font;
        private RenderFont? _arrowFont;
        private RenderFont? _minorArrowFont;
        private string _lastFontFamily = "";
        private int _lastFontSize = -1;
        private int _lastMajorArrowSize = -1;
        private int _lastMinorArrowSize = -1;

        private RenderPen? _pocPen;
        private Color _lastPocBorderColor = Color.Empty;
        private int _lastPocBorderWidth = -1;

        private RenderPen? _gridPen;
        private Color _lastGridLineColor = Color.Empty;

        // Cached Candle Overlay Pens
        private RenderPen? _bullWickPen;
        private RenderPen? _bearWickPen;
        private RenderPen? _bullOutlinePen;
        private RenderPen? _bearOutlinePen;
        private Color _lastBullColor = Color.Empty;
        private Color _lastBearColor = Color.Empty;
        private int _lastWickWidth = -1;

        // Cached Stacked Imbalance Pens
        private RenderPen? _buyImbalancePen;
        private RenderPen? _sellImbalancePen;
        private Color _lastBuyImbalanceColor = Color.Empty;
        private Color _lastSellImbalanceColor = Color.Empty;
        private int _lastImbalanceLineWidth = -1;



        // Data Caches
        private readonly Dictionary<int, decimal> _cdDayCache = new Dictionary<int, decimal>();
        private readonly List<ImbalanceLine> _imbalanceLines = new List<ImbalanceLine>();
        private readonly List<DivergencePoint> _divergences = new List<DivergencePoint>();

        // Profile & Theme State Management
        private IndicatorProfile _activeProfile = IndicatorProfile.Default;
        private ColorThemeMode _colorTheme = ColorThemeMode.DarkMode;
        private string _profileLabel = "NQ | RTH | 09:30-16:00 ET";
        private bool _isApplyingProfile = false;
        private int _sessionResetHour = 9;
        private int _sessionResetMinute = 30;

        // ----------------------------------------------------
        // Backing Fields for Settings
        // ----------------------------------------------------
        private string _fontFamily = "Arial";
        private int _fontSize = 9;
        private int _ticksGrouping = 1;
        private int _minBarWidthForText = 35;
        private decimal _purpleThreshold = 1000m;
        private Color _purpleColor = Color.FromArgb(120, 102, 51, 153);
        private decimal _orangeThreshold = 1500m;
        private Color _orangeColor = Color.FromArgb(150, 230, 126, 34);
        private Color _defaultBgColor = Color.FromArgb(40, 128, 128, 128);
        private Color _volumeTextColor = Color.FromArgb(220, 220, 220);
        private Color _volumeHighlightedTextColor = Color.White;
        private Color _positiveDeltaColor = Color.FromArgb(46, 204, 113);
        private Color _negativeDeltaColor = Color.FromArgb(231, 76, 60);
        private Color _neutralDeltaColor = Color.Gray;
        private bool _highlightPoc = true;
        private Color _pocBorderColor = Color.RoyalBlue;
        private int _pocBorderWidth = 2;
        private bool _drawGridLines = true;
        private Color _gridLineColor = Color.FromArgb(60, 128, 128, 128);

        private bool _showCandleInMiddle = true;
        private int _candleWidth = 6;
        private int _wickWidth = 1;
        private Color _bullishCandleColor = Color.FromArgb(200, 46, 204, 113);
        private Color _bearishCandleColor = Color.FromArgb(200, 231, 76, 60);

        private bool _showRightProfile = true;
        private int _rightProfileWidth = 100;
        private Color _rightProfileBgColor = Color.FromArgb(20, 128, 128, 128);
        private Color _rightProfileColorPositive = Color.FromArgb(60, 46, 204, 113);
        private Color _rightProfileColorNegative = Color.FromArgb(60, 231, 76, 60);

        private bool _showBottomStats = true;
        private Color _deltaPositiveBgColor = Color.FromArgb(120, 46, 204, 113);
        private Color _deltaNegativeBgColor = Color.FromArgb(120, 231, 76, 60);
        private Color _cdDayPositiveBgColor = Color.FromArgb(80, 46, 204, 113);
        private Color _cdDayNegativeBgColor = Color.FromArgb(80, 231, 76, 60);
        private Color _candleVolBgColor = Color.FromArgb(100, 52, 73, 94);
        private Color _statsTextColor = Color.White;
        private Color _statsLabelColor = Color.Gray;

        private bool _showImbalances = true;
        private bool _ignoreZeroValues = false;
        private decimal _imbalanceRatio = 300m;
        private int _imbalanceRange = 3;
        private decimal _imbalanceVolume = 30m;
        private int _daysLookBack = 20;

        private bool _lineTillTouch = false;
        private Color _askBidImbalanceColor = Color.FromArgb(255, 8, 153, 129);
        private Color _bidAskImbalanceColor = Color.FromArgb(255, 195, 1, 1);
        private int _lineWidth = 10;
        private int _printLineForXBars = 10;

        // Delta Divergence Backing Fields
        private bool _showDivergence = true;
        private decimal _deltaPercentageThreshold = 10m;
        private bool _showMinorDivergence = true;
        private decimal _minorDeltaPercentageThreshold = 2.5m;
        private int _majorArrowSize = 15;
        private int _minorArrowSize = 9;
        private Color _bullishDivergenceColor = Color.FromArgb(255, 46, 204, 113);
        private Color _bearishDivergenceColor = Color.FromArgb(255, 231, 76, 60);
        private Color _minorBullishDivergenceColor = Color.FromArgb(255, 120, 220, 150);
        private Color _minorBearishDivergenceColor = Color.FromArgb(255, 240, 140, 130);
        private int _divergenceDaysLookBack = 20;
        private int _maxDivergenceArrows = 100;

        // Delta Divergence Invalidation Backing Fields
        private bool _markInvalidatedDivergences = true;
        private int _invalidationLookbackBars = 2;
        private Color _invalidatedArrowColor = Color.FromArgb(100, 150, 150, 150);

        // ----------------------------------------------------
        // Profile Name Converter for Dynamic Dropdown Display
        // ----------------------------------------------------
        public class ProfileNameConverter : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            {
                var list = GetAvailableProfileDisplayNames();
                return new StandardValuesCollection(list);
            }
        }

        private static readonly Dictionary<IndicatorProfile, string> _profileLabelsMap = new Dictionary<IndicatorProfile, string>();

        public static void RefreshProfileLabelsFromDisk()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "Indicators", "Profiles");
                foreach (IndicatorProfile p in Enum.GetValues(typeof(IndicatorProfile)))
                {
                    string defaultLabel = GetDefaultProfileLabel(p);
                    _profileLabelsMap[p] = defaultLabel;

                    // The four trading presets use fixed names so the market and
                    // session can never become ambiguous in Quick Setup.
                    if (IsBuiltInTradingProfile(p))
                        continue;

                    string filepath = Path.Combine(folder, $"TotalVolDeltaFootprint_{p}.cfg");
                    if (File.Exists(filepath))
                    {
                        var lines = File.ReadAllLines(filepath);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("ProfileLabel="))
                            {
                                string lbl = line.Substring("ProfileLabel=".Length).Trim();
                                if (!string.IsNullOrEmpty(lbl))
                                {
                                    _profileLabelsMap[p] = lbl;
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Suppress
            }
        }

        public static List<string> GetAvailableProfileDisplayNames()
        {
            RefreshProfileLabelsFromDisk();
            var list = new List<string>();
            for (int slot = (int)IndicatorProfile.Default; slot <= (int)IndicatorProfile.Profile3; slot++)
            {
                var p = (IndicatorProfile)slot;
                list.Add(GetDisplayNameForProfile(p));
            }
            return list;
        }

        public static string GetDisplayNameForProfile(IndicatorProfile profile)
        {
            if (IsBuiltInTradingProfile(profile))
            {
                int fixedSlot = (int)profile + 1;
                return $"{fixedSlot}. {GetDefaultProfileLabel(profile)}";
            }

            if (!_profileLabelsMap.TryGetValue(profile, out string? label) || string.IsNullOrEmpty(label))
            {
                label = GetDefaultProfileLabel(profile);
            }

            int slotNum = (int)profile + 1;
            return $"{slotNum}. {label}";
        }

        public static IndicatorProfile ParseProfileFromDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return IndicatorProfile.Default;

            if (displayName.Length >= 2 && char.IsDigit(displayName[0]) && displayName[1] == '.')
            {
                if (int.TryParse(displayName[0].ToString(), out int slot) && slot >= 1 && slot <= 4)
                {
                    return (IndicatorProfile)(slot - 1);
                }
            }

            foreach (var kvp in _profileLabelsMap)
            {
                if (!IsBuiltInTradingProfile(kvp.Key))
                    continue;

                if (GetDisplayNameForProfile(kvp.Key) == displayName || kvp.Value == displayName || kvp.Key.ToString() == displayName)
                {
                    return kvp.Key;
                }
            }

            return IndicatorProfile.Default;
        }

        // ----------------------------------------------------
        // Profile Management Settings
        // ----------------------------------------------------
        private void SelectProfile(IndicatorProfile targetProfile)
        {
            if (_activeProfile != targetProfile && !_isApplyingProfile)
            {
                // Save settings of current active profile before switching
                SaveProfileSettings(_activeProfile);

                _activeProfile = targetProfile;

                // Load settings of the new profile
                LoadProfileSettings(_activeProfile);

                // Re-calculate and redraw chart to apply
                RecalculateValues();
                RedrawChart();

                // Empty property name follows INotifyPropertyChanged convention:
                // refresh every value shown in the ATAS property grid after a preset switch.
                OnPropertyChanged(string.Empty);
                OnPropertyChanged(nameof(ProfileLabel));
                OnPropertyChanged(nameof(ActivePresetScope));
                OnPropertyChanged(nameof(ActiveCalibrationStatus));
                OnPropertyChanged(nameof(QuickProfile));
                OnPropertyChanged(nameof(ActiveProfile));
            }
        }

        [Display(Name = "1) Profile", GroupName = QuickSetupGroup, Order = 0,
            Description = "Choose one of four fixed NQ/ES presets from the dropdown. Typing custom text is disabled.")]
        public QuickTradingProfile QuickProfile
        {
            get => ToQuickTradingProfile(_activeProfile);
            set => SelectProfile(ToIndicatorProfile(value));
        }

        // Retained only to migrate saved workspaces from the former text-based
        // selector. It is deliberately hidden from the ATAS settings panel.
        [Browsable(false)]
        public string ActiveProfile
        {
            get => GetDisplayNameForProfile(_activeProfile);
            set => SelectProfile(ParseProfileFromDisplayName(value));
        }

        [ReadOnly(true)]
        [Display(Name = "2) Active Session", GroupName = QuickSetupGroup, Order = 1,
            Description = "Set the ATAS chart time zone/session template to the same US Eastern hours.")]
        public string ActivePresetScope => GetProfileScope(_activeProfile);

        [ReadOnly(true)]
        [Display(Name = "3) Validation Status", GroupName = QuickSetupGroup, Order = 2,
            Description = "USER-TUNED means the current NQ RTH baseline is retained. RECOMMENDED BASELINE means paper-test and walk-forward validation are still required.")]
        public string ActiveCalibrationStatus => GetCalibrationStatus(_activeProfile);

        [Browsable(false)]
        public string ProfileLabel
        {
            get => _profileLabel;
            set
            {
                string normalized = IsBuiltInTradingProfile(_activeProfile)
                    ? GetDefaultProfileLabel(_activeProfile)
                    : value;

                if (_profileLabel != normalized)
                {
                    _profileLabel = normalized;
                    _profileLabelsMap[_activeProfile] = normalized;
                    if (!_isApplyingProfile)
                    {
                        SaveProfileSettings(_activeProfile);
                        OnPropertyChanged(nameof(ActiveProfile));
                        OnPropertyChanged(nameof(ProfileLabel));
                    }
                }
            }
        }

        [Display(Name = "CD Reset Hour (Chart Time)", GroupName = "10. ADVANCED SESSION", Order = 560,
            Description = "Hour when cumulative delta starts a new session. Keep the ATAS chart time zone aligned with the preset (US Eastern).")]
        [Range(0, 23)]
        public int SessionResetHour
        {
            get => _sessionResetHour;
            set { if (_sessionResetHour != value) { _sessionResetHour = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "CD Reset Minute", GroupName = "10. ADVANCED SESSION", Order = 570,
            Description = "Minute when cumulative delta starts a new session.")]
        [Range(0, 59)]
        public int SessionResetMinute
        {
            get => _sessionResetMinute;
            set { if (_sessionResetMinute != value) { _sessionResetMinute = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        // ----------------------------------------------------
        // Theme Preset Settings (Dark Mode / Light Mode / Custom)
        // ----------------------------------------------------
        [Display(Name = "Color Theme", GroupName = "02. THEME & FONT", Order = 5)]
        public ColorThemeMode ColorTheme
        {
            get => _colorTheme;
            set
            {
                if (_colorTheme != value)
                {
                    _colorTheme = value;
                    if (value == ColorThemeMode.DarkMode)
                    {
                        ApplyDarkModeColors();
                    }
                    else if (value == ColorThemeMode.LightMode)
                    {
                        ApplyLightModeColors();
                    }

                    if (!_isApplyingProfile)
                    {
                        RedrawChart();
                    }
                }
            }
        }

        // ----------------------------------------------------
        // User Settings (Configurable in ATAS Settings Panel)
        // ----------------------------------------------------
        
        [Display(Name = "Font Family", GroupName = "02. THEME & FONT", Order = 10)]
        public string FontFamily
        {
            get => _fontFamily;
            set { if (_fontFamily != value) { _fontFamily = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Font Size", GroupName = "02. THEME & FONT", Order = 20)]
        [Range(6, 30)]
        public int FontSize
        {
            get => _fontSize;
            set { if (_fontSize != value) { _fontSize = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Ticks Grouping", GroupName = "03. FOOTPRINT", Order = 25,
            Description = "Number of price ticks combined into one footprint row. The value is saved independently for each market/session preset.")]
        [Range(1, 100)]
        public int TicksGrouping
        {
            get => _ticksGrouping;
            set { if (_ticksGrouping != value) { _ticksGrouping = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Min Width for Text", GroupName = "03. FOOTPRINT", Order = 26)]
        [Range(10, 200)]
        public int MinBarWidthForText
        {
            get => _minBarWidthForText;
            set { if (_minBarWidthForText != value) { _minBarWidthForText = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "High Volume Threshold (Purple)", GroupName = "03. FOOTPRINT", Order = 30,
            Description = "Absolute contracts per grouped footprint row. This depends on bar type, timeframe, data feed, instrument and session; calibrate with your own sample.")]
        [Range(typeof(decimal), "0", "1000000000")]
        public decimal PurpleThreshold
        {
            get => _purpleThreshold;
            set
            {
                if (_purpleThreshold == value) return;
                _purpleThreshold = value;
                if (_orangeThreshold < value)
                {
                    _orangeThreshold = value;
                    OnPropertyChanged(nameof(OrangeThreshold));
                }
                if (!_isApplyingProfile) RedrawChart();
            }
        }

        [Display(Name = "High Volume Color", GroupName = "03. FOOTPRINT", Order = 40)]
        public Color PurpleColor
        {
            get => _purpleColor;
            set { if (_purpleColor != value) { _purpleColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Extreme Volume Threshold (Orange)", GroupName = "03. FOOTPRINT", Order = 50,
            Description = "Absolute contracts per grouped footprint row. Keep this above the purple threshold.")]
        [Range(typeof(decimal), "0", "1000000000")]
        public decimal OrangeThreshold
        {
            get => _orangeThreshold;
            set
            {
                decimal normalized = Math.Max(value, _purpleThreshold);
                if (_orangeThreshold == normalized) return;
                _orangeThreshold = normalized;
                if (!_isApplyingProfile) RedrawChart();
            }
        }

        [Display(Name = "Extreme Volume Color", GroupName = "03. FOOTPRINT", Order = 60)]
        public Color OrangeColor
        {
            get => _orangeColor;
            set { if (_orangeColor != value) { _orangeColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Cell Background", GroupName = "03. FOOTPRINT", Order = 70)]
        public Color DefaultBgColor
        {
            get => _defaultBgColor;
            set { if (_defaultBgColor != value) { _defaultBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Volume Text", GroupName = "03. FOOTPRINT", Order = 80)]
        public Color VolumeTextColor
        {
            get => _volumeTextColor;
            set { if (_volumeTextColor != value) { _volumeTextColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Highlighted Volume Text", GroupName = "03. FOOTPRINT", Order = 90)]
        public Color VolumeHighlightedTextColor
        {
            get => _volumeHighlightedTextColor;
            set { if (_volumeHighlightedTextColor != value) { _volumeHighlightedTextColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Positive Delta", GroupName = "03. FOOTPRINT", Order = 100)]
        public Color PositiveDeltaColor
        {
            get => _positiveDeltaColor;
            set { if (_positiveDeltaColor != value) { _positiveDeltaColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Negative Delta", GroupName = "03. FOOTPRINT", Order = 110)]
        public Color NegativeDeltaColor
        {
            get => _negativeDeltaColor;
            set { if (_negativeDeltaColor != value) { _negativeDeltaColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Neutral Delta", GroupName = "03. FOOTPRINT", Order = 120)]
        public Color NeutralDeltaColor
        {
            get => _neutralDeltaColor;
            set { if (_neutralDeltaColor != value) { _neutralDeltaColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Highlight POC", GroupName = "04. POC & GRID", Order = 130)]
        public bool HighlightPoc
        {
            get => _highlightPoc;
            set { if (_highlightPoc != value) { _highlightPoc = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "POC Border Color", GroupName = "04. POC & GRID", Order = 140)]
        public Color PocBorderColor
        {
            get => _pocBorderColor;
            set { if (_pocBorderColor != value) { _pocBorderColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "POC Border Width", GroupName = "04. POC & GRID", Order = 150)]
        [Range(1, 10)]
        public int PocBorderWidth
        {
            get => _pocBorderWidth;
            set { if (_pocBorderWidth != value) { _pocBorderWidth = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Draw Grid Lines", GroupName = "04. POC & GRID", Order = 160)]
        public bool DrawGridLines
        {
            get => _drawGridLines;
            set { if (_drawGridLines != value) { _drawGridLines = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Grid Line Color", GroupName = "04. POC & GRID", Order = 170)]
        public Color GridLineColor
        {
            get => _gridLineColor;
            set { if (_gridLineColor != value) { _gridLineColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        // ----------------------------------------------------
        // Candle in Middle Settings
        // ----------------------------------------------------
        [Display(Name = "Show Middle Candle", GroupName = "05. MIDDLE CANDLE", Order = 180)]
        public bool ShowCandleInMiddle
        {
            get => _showCandleInMiddle;
            set { if (_showCandleInMiddle != value) { _showCandleInMiddle = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Body Width", GroupName = "05. MIDDLE CANDLE", Order = 190)]
        [Range(1, 30)]
        public int CandleWidth
        {
            get => _candleWidth;
            set { if (_candleWidth != value) { _candleWidth = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Wick Width", GroupName = "05. MIDDLE CANDLE", Order = 200)]
        [Range(1, 10)]
        public int WickWidth
        {
            get => _wickWidth;
            set { if (_wickWidth != value) { _wickWidth = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Bullish Color", GroupName = "05. MIDDLE CANDLE", Order = 210)]
        public Color BullishCandleColor
        {
            get => _bullishCandleColor;
            set { if (_bullishCandleColor != value) { _bullishCandleColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Bearish Color", GroupName = "05. MIDDLE CANDLE", Order = 220)]
        public Color BearishCandleColor
        {
            get => _bearishCandleColor;
            set { if (_bearishCandleColor != value) { _bearishCandleColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        // ----------------------------------------------------
        // Right-side Profile Settings
        // ----------------------------------------------------
        [Display(Name = "Show Right Profile", GroupName = "06. RIGHT PROFILE", Order = 230)]
        public bool ShowRightProfile
        {
            get => _showRightProfile;
            set { if (_showRightProfile != value) { _showRightProfile = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Width (pixels)", GroupName = "06. RIGHT PROFILE", Order = 240)]
        [Range(40, 500)]
        public int RightProfileWidth
        {
            get => _rightProfileWidth;
            set { if (_rightProfileWidth != value) { _rightProfileWidth = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Background", GroupName = "06. RIGHT PROFILE", Order = 250)]
        public Color RightProfileBgColor
        {
            get => _rightProfileBgColor;
            set { if (_rightProfileBgColor != value) { _rightProfileBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Positive Delta", GroupName = "06. RIGHT PROFILE", Order = 260)]
        public Color RightProfileColorPositive
        {
            get => _rightProfileColorPositive;
            set { if (_rightProfileColorPositive != value) { _rightProfileColorPositive = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Negative Delta", GroupName = "06. RIGHT PROFILE", Order = 270)]
        public Color RightProfileColorNegative
        {
            get => _rightProfileColorNegative;
            set { if (_rightProfileColorNegative != value) { _rightProfileColorNegative = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        // ----------------------------------------------------
        // Bottom Stats Settings
        // ----------------------------------------------------
        [Display(Name = "Show Bottom Stats", GroupName = "07. BOTTOM STATISTICS", Order = 280)]
        public bool ShowBottomStats
        {
            get => _showBottomStats;
            set { if (_showBottomStats != value) { _showBottomStats = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Positive Delta Background", GroupName = "07. BOTTOM STATISTICS", Order = 290)]
        public Color DeltaPositiveBgColor
        {
            get => _deltaPositiveBgColor;
            set { if (_deltaPositiveBgColor != value) { _deltaPositiveBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Negative Delta Background", GroupName = "07. BOTTOM STATISTICS", Order = 300)]
        public Color DeltaNegativeBgColor
        {
            get => _deltaNegativeBgColor;
            set { if (_deltaNegativeBgColor != value) { _deltaNegativeBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Positive CD Background", GroupName = "07. BOTTOM STATISTICS", Order = 310)]
        public Color CdDayPositiveBgColor
        {
            get => _cdDayPositiveBgColor;
            set { if (_cdDayPositiveBgColor != value) { _cdDayPositiveBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Negative CD Background", GroupName = "07. BOTTOM STATISTICS", Order = 320)]
        public Color CdDayNegativeBgColor
        {
            get => _cdDayNegativeBgColor;
            set { if (_cdDayNegativeBgColor != value) { _cdDayNegativeBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Candle Volume Background", GroupName = "07. BOTTOM STATISTICS", Order = 330)]
        public Color CandleVolBgColor
        {
            get => _candleVolBgColor;
            set { if (_candleVolBgColor != value) { _candleVolBgColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Value Text", GroupName = "07. BOTTOM STATISTICS", Order = 340)]
        public Color StatsTextColor
        {
            get => _statsTextColor;
            set { if (_statsTextColor != value) { _statsTextColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Label Text", GroupName = "07. BOTTOM STATISTICS", Order = 350)]
        public Color StatsLabelColor
        {
            get => _statsLabelColor;
            set { if (_statsLabelColor != value) { _statsLabelColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        // ----------------------------------------------------
        // Stacked Imbalance Settings (Data tab)
        // ----------------------------------------------------
        [Display(Name = "Show Stacked Imbalances", GroupName = "08. STACKED IMBALANCE", Order = 390)]
        public bool ShowImbalances
        {
            get => _showImbalances;
            set { if (_showImbalances != value) { _showImbalances = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Ignore Zero Values", GroupName = "08. STACKED IMBALANCE", Order = 400)]
        public bool IgnoreZeroValues
        {
            get => _ignoreZeroValues;
            set { if (_ignoreZeroValues != value) { _ignoreZeroValues = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Ratio (%)", GroupName = "08. STACKED IMBALANCE", Order = 410,
            Description = "Diagonal Ask/Bid comparison at adjacent raw tick levels. ATAS uses 150% by default; 300% is a conservative strong-imbalance baseline.")]
        [Range(typeof(decimal), "100", "1000")]
        public decimal ImbalanceRatio
        {
            get => _imbalanceRatio;
            set { if (_imbalanceRatio != value) { _imbalanceRatio = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Consecutive Raw-Tick Levels", GroupName = "08. STACKED IMBALANCE", Order = 420,
            Description = "Number of adjacent one-tick imbalances required for a stack. Kept independent from footprint display grouping.")]
        [Range(2, 10)]
        public int ImbalanceRange
        {
            get => _imbalanceRange;
            set { if (_imbalanceRange != value) { _imbalanceRange = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Minimum Volume per Raw Tick", GroupName = "08. STACKED IMBALANCE", Order = 430,
            Description = "Rejects mathematically large ratios caused by tiny prints. This absolute filter requires instrument/session calibration.")]
        [Range(typeof(decimal), "0", "1000000")]
        public decimal ImbalanceVolume
        {
            get => _imbalanceVolume;
            set { if (_imbalanceVolume != value) { _imbalanceVolume = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Days Look Back", GroupName = "08. STACKED IMBALANCE", Order = 440)]
        [Range(1, 365)]
        public int DaysLookBack
        {
            get => _daysLookBack;
            set { if (_daysLookBack != value) { _daysLookBack = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        // ----------------------------------------------------
        // Stacked Imbalance Drawing Settings
        // ----------------------------------------------------
        [Display(Name = "Line Till Touch", GroupName = "08. STACKED IMBALANCE", Order = 450)]
        public bool LineTillTouch
        {
            get => _lineTillTouch;
            set { if (_lineTillTouch != value) { _lineTillTouch = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Buy Imbalance Color", GroupName = "08. STACKED IMBALANCE", Order = 460)]
        public Color AskBidImbalanceColor
        {
            get => _askBidImbalanceColor;
            set { if (_askBidImbalanceColor != value) { _askBidImbalanceColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Sell Imbalance Color", GroupName = "08. STACKED IMBALANCE", Order = 470)]
        public Color BidAskImbalanceColor
        {
            get => _bidAskImbalanceColor;
            set { if (_bidAskImbalanceColor != value) { _bidAskImbalanceColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Line Width", GroupName = "08. STACKED IMBALANCE", Order = 480)]
        [Range(1, 20)]
        public int LineWidth
        {
            get => _lineWidth;
            set { if (_lineWidth != value) { _lineWidth = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Maximum Line Bars", GroupName = "08. STACKED IMBALANCE", Order = 490)]
        [Range(1, 1000)]
        public int PrintLineForXBars
        {
            get => _printLineForXBars;
            set { if (_printLineForXBars != value) { _printLineForXBars = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        // ----------------------------------------------------
        // Delta Divergence Settings
        // ----------------------------------------------------
        [Display(Name = "Show Major Divergence", GroupName = "09. DELTA DIVERGENCE", Order = 500)]
        public bool ShowDivergence
        {
            get => _showDivergence;
            set { if (_showDivergence != value) { _showDivergence = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Major Delta Threshold (%)", GroupName = "09. DELTA DIVERGENCE", Order = 510)]
        [Range(typeof(decimal), "0", "100")]
        public decimal DeltaPercentageThreshold
        {
            get => _deltaPercentageThreshold;
            set
            {
                if (_deltaPercentageThreshold == value) return;
                _deltaPercentageThreshold = value;
                if (_minorDeltaPercentageThreshold > value)
                {
                    _minorDeltaPercentageThreshold = value;
                    OnPropertyChanged(nameof(MinorDeltaPercentageThreshold));
                }
                if (!_isApplyingProfile) RecalculateValues();
            }
        }

        [Display(Name = "Show Minor Divergence", GroupName = "09. DELTA DIVERGENCE", Order = 512)]
        public bool ShowMinorDivergence
        {
            get => _showMinorDivergence;
            set { if (_showMinorDivergence != value) { _showMinorDivergence = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Minor Delta Threshold (%)", GroupName = "09. DELTA DIVERGENCE", Order = 514)]
        [Range(typeof(decimal), "0", "100")]
        public decimal MinorDeltaPercentageThreshold
        {
            get => _minorDeltaPercentageThreshold;
            set
            {
                decimal normalized = Math.Min(value, _deltaPercentageThreshold);
                if (_minorDeltaPercentageThreshold == normalized) return;
                _minorDeltaPercentageThreshold = normalized;
                if (!_isApplyingProfile) RecalculateValues();
            }
        }

        [Display(Name = "Major Arrow Size", GroupName = "09. DELTA DIVERGENCE", Order = 522)]
        [Range(5, 50)]
        public int MajorArrowSize
        {
            get => _majorArrowSize;
            set { if (_majorArrowSize != value) { _majorArrowSize = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Minor Arrow Size", GroupName = "09. DELTA DIVERGENCE", Order = 524)]
        [Range(5, 50)]
        public int MinorArrowSize
        {
            get => _minorArrowSize;
            set { if (_minorArrowSize != value) { _minorArrowSize = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Major Bullish Color", GroupName = "09. DELTA DIVERGENCE", Order = 530)]
        public Color BullishDivergenceColor
        {
            get => _bullishDivergenceColor;
            set { if (_bullishDivergenceColor != value) { _bullishDivergenceColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Major Bearish Color", GroupName = "09. DELTA DIVERGENCE", Order = 532)]
        public Color BearishDivergenceColor
        {
            get => _bearishDivergenceColor;
            set { if (_bearishDivergenceColor != value) { _bearishDivergenceColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Minor Bullish Color", GroupName = "09. DELTA DIVERGENCE", Order = 534)]
        public Color MinorBullishDivergenceColor
        {
            get => _minorBullishDivergenceColor;
            set { if (_minorBullishDivergenceColor != value) { _minorBullishDivergenceColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Minor Bearish Color", GroupName = "09. DELTA DIVERGENCE", Order = 536)]
        public Color MinorBearishDivergenceColor
        {
            get => _minorBearishDivergenceColor;
            set { if (_minorBearishDivergenceColor != value) { _minorBearishDivergenceColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Dim Invalidated Signals", GroupName = "09. DELTA DIVERGENCE", Order = 537)]
        public bool MarkInvalidatedDivergences
        {
            get => _markInvalidatedDivergences;
            set { if (_markInvalidatedDivergences != value) { _markInvalidatedDivergences = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Invalidation Window (Bars)", GroupName = "09. DELTA DIVERGENCE", Order = 538)]
        [Range(1, 10)]
        public int InvalidationLookbackBars
        {
            get => _invalidationLookbackBars;
            set { if (_invalidationLookbackBars != value) { _invalidationLookbackBars = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Invalidated Arrow Color", GroupName = "09. DELTA DIVERGENCE", Order = 539)]
        public Color InvalidatedArrowColor
        {
            get => _invalidatedArrowColor;
            set { if (_invalidatedArrowColor != value) { _invalidatedArrowColor = value; _colorTheme = ColorThemeMode.Custom; if (!_isApplyingProfile) RedrawChart(); } }
        }

        [Display(Name = "Days Look Back", GroupName = "09. DELTA DIVERGENCE", Order = 545)]
        [Range(1, 365)]
        public int DivergenceDaysLookBack
        {
            get => _divergenceDaysLookBack;
            set { if (_divergenceDaysLookBack != value) { _divergenceDaysLookBack = value; if (!_isApplyingProfile) RecalculateValues(); } }
        }

        [Display(Name = "Maximum Arrows", GroupName = "09. DELTA DIVERGENCE", Order = 550)]
        [Range(1, 5000)]
        public int MaxDivergenceArrows
        {
            get => _maxDivergenceArrows;
            set { if (_maxDivergenceArrows != value) { _maxDivergenceArrows = value; if (!_isApplyingProfile) RedrawChart(); } }
        }

        // ----------------------------------------------------
        // Color Theme Presets Logic
        // ----------------------------------------------------
        public void ApplyDarkModeColors()
        {
            _defaultBgColor = Color.FromArgb(40, 128, 128, 128);
            _volumeTextColor = Color.FromArgb(220, 220, 220);
            _volumeHighlightedTextColor = Color.White;
            _purpleColor = Color.FromArgb(120, 102, 51, 153);
            _orangeColor = Color.FromArgb(150, 230, 126, 34);
            _positiveDeltaColor = Color.FromArgb(46, 204, 113);
            _negativeDeltaColor = Color.FromArgb(231, 76, 60);
            _neutralDeltaColor = Color.Gray;
            _pocBorderColor = Color.RoyalBlue;
            _gridLineColor = Color.FromArgb(60, 128, 128, 128);
            _bullishCandleColor = Color.FromArgb(200, 46, 204, 113);
            _bearishCandleColor = Color.FromArgb(200, 231, 76, 60);
            _rightProfileBgColor = Color.FromArgb(20, 128, 128, 128);
            _rightProfileColorPositive = Color.FromArgb(60, 46, 204, 113);
            _rightProfileColorNegative = Color.FromArgb(60, 231, 76, 60);
            _deltaPositiveBgColor = Color.FromArgb(120, 46, 204, 113);
            _deltaNegativeBgColor = Color.FromArgb(120, 231, 76, 60);
            _cdDayPositiveBgColor = Color.FromArgb(80, 46, 204, 113);
            _cdDayNegativeBgColor = Color.FromArgb(80, 231, 76, 60);
            _candleVolBgColor = Color.FromArgb(100, 52, 73, 94);
            _statsTextColor = Color.White;
            _statsLabelColor = Color.Gray;
            _askBidImbalanceColor = Color.FromArgb(255, 8, 153, 129);
            _bidAskImbalanceColor = Color.FromArgb(255, 195, 1, 1);
            _bullishDivergenceColor = Color.FromArgb(255, 46, 204, 113);
            _bearishDivergenceColor = Color.FromArgb(255, 231, 76, 60);
            _minorBullishDivergenceColor = Color.FromArgb(255, 120, 220, 150);
            _minorBearishDivergenceColor = Color.FromArgb(255, 240, 140, 130);
            _invalidatedArrowColor = Color.FromArgb(100, 150, 150, 150);
        }

        public void ApplyLightModeColors()
        {
            _defaultBgColor = Color.FromArgb(30, 200, 205, 215);
            _volumeTextColor = Color.FromArgb(40, 44, 52); // Crisp dark charcoal for white background
            _volumeHighlightedTextColor = Color.White;
            _purpleColor = Color.FromArgb(180, 142, 68, 173); // Rich royal purple
            _orangeColor = Color.FromArgb(200, 230, 126, 34); // Vibrant rich orange
            _positiveDeltaColor = Color.FromArgb(16, 142, 68); // Deep forest green for high contrast on light bg
            _negativeDeltaColor = Color.FromArgb(215, 38, 56); // Deep ruby red for high contrast on light bg
            _neutralDeltaColor = Color.FromArgb(110, 120, 130);
            _pocBorderColor = Color.FromArgb(255, 30, 100, 220); // Vibrant blue POC border
            _gridLineColor = Color.FromArgb(70, 180, 185, 195);
            _bullishCandleColor = Color.FromArgb(220, 16, 142, 68);
            _bearishCandleColor = Color.FromArgb(220, 215, 38, 56);
            _rightProfileBgColor = Color.FromArgb(25, 150, 160, 175);
            _rightProfileColorPositive = Color.FromArgb(75, 16, 142, 68);
            _rightProfileColorNegative = Color.FromArgb(75, 215, 38, 56);
            _deltaPositiveBgColor = Color.FromArgb(140, 16, 142, 68);
            _deltaNegativeBgColor = Color.FromArgb(140, 215, 38, 56);
            _cdDayPositiveBgColor = Color.FromArgb(100, 16, 142, 68);
            _cdDayNegativeBgColor = Color.FromArgb(100, 215, 38, 56);
            _candleVolBgColor = Color.FromArgb(120, 90, 105, 120);
            _statsTextColor = Color.White;
            _statsLabelColor = Color.FromArgb(70, 80, 90);
            _askBidImbalanceColor = Color.FromArgb(255, 16, 142, 68);
            _bidAskImbalanceColor = Color.FromArgb(255, 215, 38, 56);
            _bullishDivergenceColor = Color.FromArgb(255, 16, 142, 68);
            _bearishDivergenceColor = Color.FromArgb(255, 215, 38, 56);
            _minorBullishDivergenceColor = Color.FromArgb(255, 80, 180, 120);
            _minorBearishDivergenceColor = Color.FromArgb(255, 235, 100, 110);
            _invalidatedArrowColor = Color.FromArgb(120, 180, 185, 195);
        }

        // ----------------------------------------------------
        // Built-in NQ / ES Session Presets
        // ----------------------------------------------------
        private void ApplyBuiltInProfileDefaults(IndicatorProfile profile)
        {
            _isApplyingProfile = true;

            // Keep all four trading presets visually consistent. Only the
            // liquidity-sensitive parameters change between instrument/session.
            ApplyLightModeColors();
            _colorTheme = ColorThemeMode.LightMode;
            _profileLabel = GetDefaultProfileLabel(profile);
            _profileLabelsMap[profile] = _profileLabel;

            _fontFamily = "Arial";
            _fontSize = 9;
            _minBarWidthForText = 35;
            _highlightPoc = true;
            _pocBorderWidth = 2;
            _drawGridLines = false;
            _showCandleInMiddle = false;
            _candleWidth = 6;
            _wickWidth = 1;
            _showRightProfile = false;
            _rightProfileWidth = 100;
            _showBottomStats = false;

            _showImbalances = true;
            _ignoreZeroValues = true;
            _daysLookBack = 20;
            _lineTillTouch = true;
            _lineWidth = 1;
            _printLineForXBars = 1;

            _showDivergence = true;
            _showMinorDivergence = true;
            _majorArrowSize = 15;
            _minorArrowSize = 9;
            _markInvalidatedDivergences = true;
            _invalidationLookbackBars = 2;
            _divergenceDaysLookBack = 20;
            _maxDivergenceArrows = 100;

            switch (profile)
            {
                case IndicatorProfile.Default: // NQ RTH
                    _sessionResetHour = 9;
                    _sessionResetMinute = 30;
                    _ticksGrouping = 12;
                    _purpleThreshold = 150m;
                    _orangeThreshold = 300m;
                    _imbalanceRatio = 280m;
                    _imbalanceRange = 2;
                    _imbalanceVolume = 20m;
                    _deltaPercentageThreshold = 10m;
                    _minorDeltaPercentageThreshold = 2m;
                    break;

                case IndicatorProfile.Profile1: // NQ ETH
                    _sessionResetHour = 18;
                    _sessionResetMinute = 0;
                    _ticksGrouping = 8;
                    _purpleThreshold = 60m;
                    _orangeThreshold = 120m;
                    _imbalanceRatio = 300m;
                    _imbalanceRange = 3;
                    _imbalanceVolume = 8m;
                    _deltaPercentageThreshold = 12m;
                    _minorDeltaPercentageThreshold = 3m;
                    break;

                case IndicatorProfile.Profile2: // ES RTH
                    _sessionResetHour = 9;
                    _sessionResetMinute = 30;
                    _ticksGrouping = 4;
                    _purpleThreshold = 300m;
                    _orangeThreshold = 600m;
                    _imbalanceRatio = 300m;
                    _imbalanceRange = 3;
                    _imbalanceVolume = 40m;
                    _deltaPercentageThreshold = 8m;
                    _minorDeltaPercentageThreshold = 2m;
                    break;

                case IndicatorProfile.Profile3: // ES ETH
                    _sessionResetHour = 18;
                    _sessionResetMinute = 0;
                    _ticksGrouping = 4;
                    _purpleThreshold = 100m;
                    _orangeThreshold = 200m;
                    _imbalanceRatio = 300m;
                    _imbalanceRange = 3;
                    _imbalanceVolume = 12m;
                    _deltaPercentageThreshold = 10m;
                    _minorDeltaPercentageThreshold = 2.5m;
                    break;

                default: // Custom slots start from a neutral footprint baseline.
                    _sessionResetHour = 0;
                    _sessionResetMinute = 0;
                    _ticksGrouping = 1;
                    _purpleThreshold = 1000m;
                    _orangeThreshold = 1500m;
                    _imbalanceRatio = 300m;
                    _imbalanceRange = 3;
                    _imbalanceVolume = 30m;
                    _deltaPercentageThreshold = 10m;
                    _minorDeltaPercentageThreshold = 2.5m;
                    break;
            }

            _isApplyingProfile = false;
        }

        // ----------------------------------------------------
        // Constructor
        // ----------------------------------------------------
        public TotalVolDeltaFootprint()
        {
            // Enable custom drawing in OnRender
            EnableCustomDrawing = true;

            // Subscribe to rendering events
            SubscribeToDrawingEvents(DrawingLayouts.LatestBar | DrawingLayouts.Final);

            // Ensure the indicator displays on the main price chart
            Panel = IndicatorDataProvider.CandlesPanel;
            DenyToChangePanel = true;

            // Refresh profile display names from saved files
            RefreshProfileLabelsFromDisk();

            // Load settings of Default profile on startup if it exists
            LoadProfileSettings(IndicatorProfile.Default);
        }

        // ----------------------------------------------------
        // OnDispose - Save current active profile before indicator exits
        // ----------------------------------------------------
        protected override void OnDispose()
        {
            SaveProfileSettings(_activeProfile);
            base.OnDispose();
        }

        private DateTime GetSessionDate(DateTime candleTime)
        {
            var resetTime = new TimeSpan(SessionResetHour, SessionResetMinute, 0);
            return candleTime.TimeOfDay >= resetTime
                ? candleTime.Date
                : candleTime.Date.AddDays(-1);
        }

        // ----------------------------------------------------
        // OnCalculate - Entry Point for Daily CD, Imbalances, & Divergences
        // ----------------------------------------------------
        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            decimal currentDelta = candle.Delta;

            if (bar == 0)
            {
                _cdDayCache.Clear();
                _cdDayCache[bar] = currentDelta;
                _imbalanceLines.Clear();
                _divergences.Clear();
            }
            else
            {
                var prevCandle = GetCandle(bar - 1);
                if (prevCandle == null || GetSessionDate(candle.Time) != GetSessionDate(prevCandle.Time))
                {
                    // Reset cumulative delta at the active profile's session boundary.
                    _cdDayCache[bar] = currentDelta;
                }
                else
                {
                    _cdDayCache.TryGetValue(bar - 1, out decimal prevCd);
                    _cdDayCache[bar] = prevCd + currentDelta;
                }
            }

            // Perform Stacked Imbalances and mitigation checks only if enabled and within DaysLookBack limit
            if (ShowImbalances)
            {
                var lastCandle = GetCandle(CurrentBar - 1);
                bool withinLookback = true;
                if (lastCandle != null)
                {
                    withinLookback = candle.Time >= lastCandle.Time.AddDays(-DaysLookBack);
                }

                if (withinLookback)
                {
                    CalculateStackedImbalances(bar);
                }
                else
                {
                    // Update mitigation states for existing lines
                    UpdateMitigations(bar);
                }
            }

            // Perform Delta Divergence calculations if enabled and within DivergenceDaysLookBack limit
            if (ShowDivergence || ShowMinorDivergence)
            {
                var lastCandle = GetCandle(CurrentBar - 1);
                bool withinLookback = true;
                if (lastCandle != null)
                {
                    withinLookback = candle.Time >= lastCandle.Time.AddDays(-DivergenceDaysLookBack);
                }

                if (withinLookback)
                {
                    CalculateDeltaDivergence(bar);
                }

                // Check and update invalidation states for existing divergence arrows
                if (MarkInvalidatedDivergences)
                {
                    UpdateDivergenceInvalidations(bar);
                }
            }
        }

        // ----------------------------------------------------
        // Helper: Calculate Stacked Imbalances for a specific bar
        // ----------------------------------------------------
        private void CalculateStackedImbalances(int bar)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            // ATAS defines diagonal imbalance between adjacent raw price levels.
            // Footprint display grouping must not widen the comparison distance.
            var levels = GetGroupedPriceLevels(candle, 1);
            int n = levels.Count;
            if (n < ImbalanceRange) return;

            bool[] isBuyImbalance = new bool[n];
            bool[] isSellImbalance = new bool[n];

            for (int i = 0; i < n; i++)
            {
                // Buy Imbalance (diagonal down comparison: Ask[i] vs Bid[i+1])
                if (i + 1 < n)
                {
                    decimal ask = levels[i].Ask;
                    decimal diagonalBid = levels[i+1].Bid;
                    if (ask >= ImbalanceVolume)
                    {
                        if (diagonalBid > 0)
                            isBuyImbalance[i] = ask >= diagonalBid * (ImbalanceRatio / 100.0m);
                        else
                            isBuyImbalance[i] = !IgnoreZeroValues;
                    }
                }

                // Sell Imbalance (diagonal up comparison: Bid[i] vs Ask[i-1])
                if (i - 1 >= 0)
                {
                    decimal bid = levels[i].Bid;
                    decimal diagonalAsk = levels[i-1].Ask;
                    if (bid >= ImbalanceVolume)
                    {
                        if (diagonalAsk > 0)
                            isSellImbalance[i] = bid >= diagonalAsk * (ImbalanceRatio / 100.0m);
                        else
                            isSellImbalance[i] = !IgnoreZeroValues;
                    }
                }
            }

            // Check Buy Stacked Imbalance
            for (int i = 0; i <= n - ImbalanceRange; i++)
            {
                bool isStacked = true;
                for (int j = 0; j < ImbalanceRange; j++)
                {
                    if (!isBuyImbalance[i + j])
                    {
                        isStacked = false;
                        break;
                    }
                }

                if (isStacked)
                {
                    for (int j = 0; j < ImbalanceRange; j++)
                    {
                        AddImbalanceLine(levels[i + j].Price, bar, true);
                    }
                }
            }

            // Check Sell Stacked Imbalance
            for (int i = 0; i <= n - ImbalanceRange; i++)
            {
                bool isStacked = true;
                for (int j = 0; j < ImbalanceRange; j++)
                {
                    if (!isSellImbalance[i + j])
                    {
                        isStacked = false;
                        break;
                    }
                }

                if (isStacked)
                {
                    for (int j = 0; j < ImbalanceRange; j++)
                    {
                        AddImbalanceLine(levels[i + j].Price, bar, false);
                    }
                }
            }

            // Update mitigation states
            UpdateMitigations(bar);
        }

        private void AddImbalanceLine(decimal price, int bar, bool isBuy)
        {
            if (_imbalanceLines.Any(l => l.StartBar == bar && l.Price == price && l.IsBuy == isBuy))
                return;

            _imbalanceLines.Add(new ImbalanceLine
            {
                Price = price,
                StartBar = bar,
                IsBuy = isBuy,
                IsMitigated = false,
                MitigatedBar = -1
            });
        }

        private void UpdateMitigations(int bar)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            foreach (var line in _imbalanceLines)
            {
                if (line.IsMitigated) continue;
                if (line.StartBar >= bar) continue; // Avoid mitigating on the same bar

                if (line.IsBuy)
                {
                    if (candle.Low <= line.Price)
                    {
                        line.IsMitigated = true;
                        line.MitigatedBar = bar;
                    }
                }
                else
                {
                    if (candle.High >= line.Price)
                    {
                        line.IsMitigated = true;
                        line.MitigatedBar = bar;
                    }
                }
            }
        }

        // ----------------------------------------------------
        // Helper: Calculate Delta Divergence for a specific bar
        // ----------------------------------------------------
        private void CalculateDeltaDivergence(int bar)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;
            if (candle.Volume <= 0) return;

            _divergences.RemoveAll(d => d.Bar == bar);

            // Calculate absolute Delta % relative to candle volume
            decimal deltaPct = (Math.Abs(candle.Delta) / candle.Volume) * 100.0m;

            bool isBullishCond = candle.Close > candle.Open && candle.Delta < 0;
            bool isBearishCond = candle.Close < candle.Open && candle.Delta > 0;

            if (!isBullishCond && !isBearishCond) return;

            // 1. Check Major Divergence
            if (ShowDivergence && deltaPct >= DeltaPercentageThreshold)
            {
                _divergences.Add(new DivergencePoint
                {
                    Bar = bar,
                    IsBullish = isBullishCond,
                    Price = isBullishCond ? candle.Low : candle.High,
                    IsMajor = true,
                    IsInvalidated = false,
                    InvalidatedBar = -1
                });
            }
            // 2. Check Minor Divergence (Only if Major did not trigger or is disabled)
            else if (ShowMinorDivergence && deltaPct >= MinorDeltaPercentageThreshold)
            {
                _divergences.Add(new DivergencePoint
                {
                    Bar = bar,
                    IsBullish = isBullishCond,
                    Price = isBullishCond ? candle.Low : candle.High,
                    IsMajor = false,
                    IsInvalidated = false,
                    InvalidatedBar = -1
                });
            }
        }

        // ----------------------------------------------------
        // Helper: Update Invalidation state for Divergence Points
        // ----------------------------------------------------
        private void UpdateDivergenceInvalidations(int bar)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            foreach (var div in _divergences)
            {
                if (div.IsInvalidated) continue;
                
                int barDiff = bar - div.Bar;
                // Only check within the immediate window (1 to InvalidationLookbackBars candles, default 2)
                if (barDiff >= 1 && barDiff <= InvalidationLookbackBars)
                {
                    var signalCandle = GetCandle(div.Bar);
                    if (signalCandle == null) continue;

                    if (div.IsBullish)
                    {
                        // Bullish signal (bottom): Invalidated if immediate next 1-2 bars break below signal candle's Low
                        if (candle.Low < signalCandle.Low)
                        {
                            div.IsInvalidated = true;
                            div.InvalidatedBar = bar;
                        }
                    }
                    else
                    {
                        // Bearish signal (top): Invalidated if immediate next 1-2 bars break above signal candle's High
                        if (candle.High > signalCandle.High)
                        {
                            div.IsInvalidated = true;
                            div.InvalidatedBar = bar;
                        }
                    }
                }
            }
        }

        // ----------------------------------------------------
        // OnRender - Custom Drawing Logic
        // ----------------------------------------------------
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            // Safety checks
            if (ChartInfo == null || ChartInfo.PriceChartContainer == null || this.InstrumentInfo == null)
                return;

            // Update fonts and pens if user settings changed (avoiding frame allocations)
            UpdateResources();

            int barWidth = (int)ChartInfo.PriceChartContainer.BarsWidth;
            if (barWidth < 2) return; // Don't render anything if candles are extremely narrow (sub-2px)

            // Determine if we should reserve vertical space for bottom stats table
            int bottomAreaY = ChartInfo.Region.Height - 54;

            // 1. Calculate and Render Right Profile Data if enabled
            var profileData = new Dictionary<decimal, (decimal Volume, decimal Delta)>();
            if (ShowRightProfile)
            {
                for (int bar = FirstVisibleBarNumber; bar <= LastVisibleBarNumber; bar++)
                {
                    if (bar < 0 || bar >= CurrentBar) continue;
                    var candle = GetCandle(bar);
                    if (candle == null) continue;

                    var candleLevels = GetGroupedPriceLevels(candle);
                    foreach (var level in candleLevels)
                    {
                        if (!profileData.TryGetValue(level.Price, out var data))
                        {
                            data = (0, 0);
                        }
                        profileData[level.Price] = (data.Volume + level.Volume, data.Delta + level.Delta);
                    }
                }

                // Render Profile Background
                if (RightProfileBgColor.A > 0)
                {
                    int profileHeight = ShowBottomStats ? bottomAreaY : ChartInfo.Region.Height;
                    var profileBgRect = new Rectangle(ChartInfo.Region.Width - RightProfileWidth, 0, RightProfileWidth, profileHeight);
                    context.FillRectangle(RightProfileBgColor, profileBgRect);
                }

                // Find max volume for scaling
                decimal maxProfileVol = 1;
                if (profileData.Count > 0)
                {
                    maxProfileVol = profileData.Values.Max(x => x.Volume);
                    if (maxProfileVol <= 0) maxProfileVol = 1;
                }

                decimal groupSize = this.InstrumentInfo.TickSize * TicksGrouping;

                // Render Profile Bars
                foreach (var entry in profileData)
                {
                    decimal price = entry.Key;
                    var levelData = entry.Value;

                    int y = ChartInfo.GetYByPrice(price + groupSize, true);
                    int yNext = ChartInfo.GetYByPrice(price, true);
                    int cellHeight = yNext - y;
                    if (cellHeight <= 0) cellHeight = 1;

                    // Skip if off-screen vertically
                    if (y < -cellHeight || y > (ShowBottomStats ? bottomAreaY : ChartInfo.Region.Height))
                        continue;

                    int profileBarWidth = (int)((levelData.Volume / maxProfileVol) * RightProfileWidth);
                    int barX = ChartInfo.Region.Width - profileBarWidth;
                    var profileBarRect = new Rectangle(barX, y, profileBarWidth, cellHeight);

                    Color barColor = levelData.Delta > 0 ? RightProfileColorPositive : (levelData.Delta < 0 ? RightProfileColorNegative : NeutralDeltaColor);
                    context.FillRectangle(barColor, profileBarRect);

                    // Draw outer border for profile
                    if (_gridPen != null)
                    {
                        context.DrawRectangle(_gridPen, profileBarRect);
                    }

                    // Draw Delta text
                    string textStr = ((int)levelData.Delta).ToString();
                    var textSize = context.MeasureString(textStr, _font!);
                    int textX = ChartInfo.Region.Width - (int)textSize.Width - 10;
                    int textY = y + (cellHeight - (int)textSize.Height) / 2;

                    Color textColor = levelData.Delta > 0 ? PositiveDeltaColor : (levelData.Delta < 0 ? NegativeDeltaColor : NeutralDeltaColor);
                    context.DrawString(textStr, _font!, textColor, textX, textY);
                }
            }

            // Determine if we should draw detailed footprint cells (background + numbers + poc + grid)
            bool drawFootprintDetails = barWidth >= MinBarWidthForText;

            // 2. Render Candle Footprints & Middle Candles
            for (int bar = FirstVisibleBarNumber; bar <= LastVisibleBarNumber; bar++)
            {
                if (bar < 0 || bar >= CurrentBar) continue;

                var candle = GetCandle(bar);
                if (candle == null) continue;

                int x = ChartInfo.GetXByBar(bar, true);

                if (drawFootprintDetails)
                {
                    var priceLevels = GetGroupedPriceLevels(candle);
                    if (priceLevels.Count > 0)
                    {
                        // Find POC level of the candle (only search levels with non-zero volume)
                        GroupedPriceLevel? pocLevel = null;
                        decimal maxVolume = -1;
                        foreach (var level in priceLevels)
                        {
                            if (level.Volume > maxVolume)
                            {
                                maxVolume = level.Volume;
                                pocLevel = level;
                            }
                        }

                        decimal groupSize = this.InstrumentInfo.TickSize * TicksGrouping;

                        // Draw each price level
                        foreach (var level in priceLevels)
                        {
                            // Skip drawing levels that have 0 volume (Optimization & Cleanliness)
                            if (level.Volume == 0 && level.Ask == 0 && level.Bid == 0)
                                continue;

                            int y = ChartInfo.GetYByPrice(level.Price + groupSize, true);
                            int yNext = ChartInfo.GetYByPrice(level.Price, true);
                            int cellHeight = yNext - y;
                            if (cellHeight <= 0) cellHeight = 1;

                            // Skip drawing if off-screen vertically or overlaps with bottom stats
                            if (y < -cellHeight || (ShowBottomStats && y >= bottomAreaY))
                                continue;

                            // Define bounding rectangles for left (Volume) and right (Delta) cells
                            int halfWidth = barWidth / 2;
                            var leftRect = new Rectangle(x, y, halfWidth, cellHeight);
                            var rightRect = new Rectangle(x + halfWidth, y, barWidth - halfWidth, cellHeight);

                            // Determine Volume background color based on thresholds
                            Color leftBgColor = DefaultBgColor;
                            Color volTextColor = VolumeTextColor;

                            if (level.Volume >= OrangeThreshold)
                            {
                                leftBgColor = OrangeColor;
                                volTextColor = VolumeHighlightedTextColor;
                            }
                            else if (level.Volume >= PurpleThreshold)
                            {
                                leftBgColor = PurpleColor;
                                volTextColor = VolumeHighlightedTextColor;
                            }

                            // Draw cell backgrounds
                            if (leftBgColor.A > 0)
                                context.FillRectangle(leftBgColor, leftRect);

                            if (DefaultBgColor.A > 0)
                                context.FillRectangle(DefaultBgColor, rightRect);

                            // Draw Grid lines
                            if (DrawGridLines && _gridPen != null)
                            {
                                context.DrawLine(_gridPen, x + halfWidth, y, x + halfWidth, y + cellHeight);
                                context.DrawLine(_gridPen, x, y, x, y + cellHeight);
                                context.DrawLine(_gridPen, x + barWidth, y, x + barWidth, y + cellHeight);
                                context.DrawLine(_gridPen, x, y + cellHeight, x + barWidth, y + cellHeight);
                            }

                            // Draw Volume text
                            string volStr = ((int)level.Volume).ToString();
                            var volSize = context.MeasureString(volStr, _font!);
                            int volX = leftRect.X + (leftRect.Width - (int)volSize.Width) / 2;
                            int volY = leftRect.Y + (leftRect.Height - (int)volSize.Height) / 2;
                            context.DrawString(volStr, _font!, volTextColor, volX, volY);

                            // Draw Delta text
                            string deltaStr = ((int)level.Delta).ToString();
                            var deltaSize = context.MeasureString(deltaStr, _font!);
                            int deltaX = rightRect.X + (rightRect.Width - (int)deltaSize.Width) / 2;
                            int deltaY = rightRect.Y + (rightRect.Height - (int)deltaSize.Height) / 2;

                            Color deltaColor = level.Delta > 0 ? PositiveDeltaColor : (level.Delta < 0 ? NegativeDeltaColor : NeutralDeltaColor);
                            context.DrawString(deltaStr, _font!, deltaColor, deltaX, deltaY);

                            // Draw POC border
                            if (HighlightPoc && level == pocLevel && _pocPen != null)
                            {
                                var pocRect = new Rectangle(x, y, barWidth, cellHeight);
                                context.DrawRectangle(_pocPen, pocRect);
                            }
                        }
                    }
                }

                // 3. Render Middle Candle (Strictly obeys ShowCandleInMiddle toggle)
                if (ShowCandleInMiddle)
                {
                    int xMid = x + barWidth / 2;
                    int yOpen = ChartInfo.GetYByPrice(candle.Open, false);
                    int yClose = ChartInfo.GetYByPrice(candle.Close, false);
                    int yHigh = ChartInfo.GetYByPrice(candle.High, false);
                    int yLow = ChartInfo.GetYByPrice(candle.Low, false);

                    Color candleColor = candle.Close >= candle.Open ? BullishCandleColor : BearishCandleColor;
                    RenderPen wickPen = candle.Close >= candle.Open ? _bullWickPen! : _bearWickPen!;
                    RenderPen bodyOutlinePen = candle.Close >= candle.Open ? _bullOutlinePen! : _bearOutlinePen!;

                    // Ensure wicks do not overlap into bottom stats
                    int maxWickY = ShowBottomStats ? bottomAreaY : ChartInfo.Region.Height;
                    if (yHigh < maxWickY)
                    {
                        int yLowClamped = Math.Min(yLow, maxWickY);
                        context.DrawLine(wickPen, xMid, yHigh, xMid, yLowClamped);
                    }

                    // Body
                    int yTop = Math.Min(yOpen, yClose);
                    int yBottom = Math.Max(yOpen, yClose);
                    
                    if (yTop < maxWickY)
                    {
                        int yBottomClamped = Math.Min(yBottom, maxWickY);
                        int bodyHeight = yBottomClamped - yTop;
                        
                        // Adaptive body width: clamp to barWidth when zoomed out to prevent overlapping
                        int currentBodyWidth = drawFootprintDetails ? CandleWidth : Math.Max(1, barWidth - 2);
                        int bodyX = xMid - currentBodyWidth / 2;

                        if (bodyHeight <= 0)
                        {
                            // Doji line using outline pen
                            context.DrawLine(bodyOutlinePen, xMid - currentBodyWidth / 2, yOpen, xMid + currentBodyWidth / 2, yOpen);
                        }
                        else
                        {
                            var bodyRect = new Rectangle(bodyX, yTop, currentBodyWidth, bodyHeight);
                            context.FillRectangle(candleColor, bodyRect);
                            context.DrawRectangle(bodyOutlinePen, bodyRect);
                        }
                    }
                }
            }

            // 4. Render Stacked Imbalance Lines (Only if enabled)
            if (ShowImbalances)
            {
                foreach (var line in _imbalanceLines)
                {
                    // Legacy behavior: once touched, remove the level entirely.
                    if (LineTillTouch && line.IsMitigated)
                        continue;

                    int endBar = CurrentBar - 1;
                    if (line.IsMitigated)
                    {
                        endBar = line.MitigatedBar;
                    }

                    if (!LineTillTouch)
                    {
                        int maxEnd = line.StartBar + PrintLineForXBars;
                        if (line.IsMitigated)
                            endBar = Math.Min(maxEnd, line.MitigatedBar);
                        else
                            endBar = Math.Min(maxEnd, CurrentBar - 1);
                    }

                    // Filter out off-screen bars (horizontal optimization)
                    if (line.StartBar > LastVisibleBarNumber || endBar < FirstVisibleBarNumber)
                        continue;

                    int startX = ChartInfo.GetXByBar(line.StartBar, true) + barWidth;
                    int endX;

                    if (line.IsMitigated && endBar == line.MitigatedBar)
                    {
                        // Mitigated, stop at the left edge of the mitigating bar
                        endX = ChartInfo.GetXByBar(endBar, true);
                    }
                    else
                    {
                        // Extend to the right edge of the end bar
                        endX = ChartInfo.GetXByBar(endBar, true) + barWidth;
                    }

                    int y = ChartInfo.GetYByPrice(line.Price, false);

                    // Skip drawing if price is off-screen vertically
                    if (y < 0 || y > (ShowBottomStats ? bottomAreaY : ChartInfo.Region.Height))
                        continue;

                    RenderPen imbalancePen = line.IsBuy ? _buyImbalancePen! : _sellImbalancePen!;
                    context.DrawLine(imbalancePen, startX, y, endX, y);
                }
            }

            // 5. Render Delta Divergence Arrows (Only if enabled)
            if (ShowDivergence || ShowMinorDivergence)
            {
                // Take only visible divergences up to MaxDivergenceArrows, starting from the most recent
                var visibleDivergences = _divergences
                    .Where(d => d.Bar >= FirstVisibleBarNumber && d.Bar <= LastVisibleBarNumber)
                    .OrderByDescending(d => d.Bar)
                    .Take(MaxDivergenceArrows)
                    .ToList();

                foreach (var div in visibleDivergences)
                {
                    var candle = GetCandle(div.Bar);
                    if (candle == null) continue;

                    int x = ChartInfo.GetXByBar(div.Bar, true);
                    int xMid = x + barWidth / 2;

                    string arrowStr = div.IsBullish ? "▲" : "▼";
                    
                    Color arrowColor;
                    if (div.IsInvalidated && MarkInvalidatedDivergences)
                    {
                        arrowColor = InvalidatedArrowColor;
                    }
                    else if (div.IsMajor)
                    {
                        arrowColor = div.IsBullish ? BullishDivergenceColor : BearishDivergenceColor;
                    }
                    else
                    {
                        arrowColor = div.IsBullish ? MinorBullishDivergenceColor : MinorBearishDivergenceColor;
                    }

                    RenderFont currentArrowFont = div.IsMajor ? _arrowFont! : _minorArrowFont!;

                    var arrowSize = context.MeasureString(arrowStr, currentArrowFont);
                    int arrowX = xMid - (int)arrowSize.Width / 2;

                    if (div.IsBullish)
                    {
                        int yLow = ChartInfo.GetYByPrice(candle.Low, false);
                        int arrowY = yLow + 5;

                        // Ensure arrow does not overlap with bottom stats
                        if (!ShowBottomStats || arrowY + (int)arrowSize.Height <= bottomAreaY)
                        {
                            context.DrawString(arrowStr, currentArrowFont, arrowColor, arrowX, arrowY);
                        }
                    }
                    else
                    {
                        int yHigh = ChartInfo.GetYByPrice(candle.High, false);
                        int arrowY = yHigh - 5 - (int)arrowSize.Height;

                        if (arrowY >= 0)
                        {
                            context.DrawString(arrowStr, currentArrowFont, arrowColor, arrowX, arrowY);
                        }
                    }
                }
            }

            // 6. Render Bottom Stats
            if (ShowBottomStats)
            {
                // Draw horizontal grid lines for stats table
                if (_gridPen != null)
                {
                    context.DrawLine(_gridPen, 0, bottomAreaY, ChartInfo.Region.Width, bottomAreaY);
                    context.DrawLine(_gridPen, 0, bottomAreaY + 18, ChartInfo.Region.Width, bottomAreaY + 18);
                    context.DrawLine(_gridPen, 0, bottomAreaY + 36, ChartInfo.Region.Width, bottomAreaY + 36);
                    context.DrawLine(_gridPen, 0, bottomAreaY + 54, ChartInfo.Region.Width, bottomAreaY + 54);
                }

                // Render columns for each visible bar
                for (int bar = FirstVisibleBarNumber; bar <= LastVisibleBarNumber; bar++)
                {
                    if (bar < 0 || bar >= CurrentBar) continue;

                    var candle = GetCandle(bar);
                    if (candle == null) continue;

                    int x = ChartInfo.GetXByBar(bar, true);

                    var deltaRect = new Rectangle(x, bottomAreaY, barWidth, 18);
                    var cdRect = new Rectangle(x, bottomAreaY + 18, barWidth, 18);
                    var volRect = new Rectangle(x, bottomAreaY + 36, barWidth, 18);

                    // Row 1: Delta
                    decimal deltaVal = candle.Delta;
                    Color deltaBg = deltaVal > 0 ? DeltaPositiveBgColor : (deltaVal < 0 ? DeltaNegativeBgColor : Color.FromArgb(40, 128, 128, 128));
                    context.FillRectangle(deltaBg, deltaRect);

                    // Row 2: CD Day
                    _cdDayCache.TryGetValue(bar, out decimal cdDayVal);
                    Color cdBg = cdDayVal > 0 ? CdDayPositiveBgColor : (cdDayVal < 0 ? CdDayNegativeBgColor : Color.FromArgb(40, 128, 128, 128));
                    context.FillRectangle(cdBg, cdRect);

                    // Row 3: Candle Vol
                    context.FillRectangle(CandleVolBgColor, volRect);

                    // Draw vertical separators
                    if (_gridPen != null)
                    {
                        context.DrawLine(_gridPen, x, bottomAreaY, x, bottomAreaY + 54);
                        context.DrawLine(_gridPen, x + barWidth, bottomAreaY, x + barWidth, bottomAreaY + 54);
                    }

                    // Draw Delta Text (Only if candle width is wide enough to avoid overlap)
                    string deltaStr = ((int)deltaVal).ToString();
                    var deltaSize = context.MeasureString(deltaStr, _font!);
                    if (barWidth >= (int)deltaSize.Width + 4)
                    {
                        int deltaX = deltaRect.X + (deltaRect.Width - (int)deltaSize.Width) / 2;
                        int deltaY = deltaRect.Y + (deltaRect.Height - (int)deltaSize.Height) / 2;
                        context.DrawString(deltaStr, _font!, StatsTextColor, deltaX, deltaY);
                    }

                    // Draw CD Day Text (Only if candle width is wide enough to avoid overlap)
                    string cdStr = ((int)cdDayVal).ToString();
                    var cdSize = context.MeasureString(cdStr, _font!);
                    if (barWidth >= (int)cdSize.Width + 4)
                    {
                        int cdX = cdRect.X + (cdRect.Width - (int)cdSize.Width) / 2;
                        int cdY = cdRect.Y + (cdRect.Height - (int)cdSize.Height) / 2;
                        context.DrawString(cdStr, _font!, StatsTextColor, cdX, cdY);
                    }

                    // Draw Candle Vol Text (Only if candle width is wide enough to avoid overlap)
                    string volStr = ((int)candle.Volume).ToString();
                    var volSize = context.MeasureString(volStr, _font!);
                    if (barWidth >= (int)volSize.Width + 4)
                    {
                        int volX = volRect.X + (volRect.Width - (int)volSize.Width) / 2;
                        int volY = volRect.Y + (volRect.Height - (int)volSize.Height) / 2;
                        context.DrawString(volStr, _font!, StatsTextColor, volX, volY);
                    }
                }

                // Draw solid background and text labels on the right edge (sticky labels)
                int labelWidth = 80;
                int labelX = ChartInfo.Region.Width - labelWidth;
                var labelBgRect = new Rectangle(labelX, bottomAreaY, labelWidth, 54);
                
                // Draw label background card for premium readability
                Color labelCardBg = _colorTheme == ColorThemeMode.LightMode ? Color.FromArgb(235, 238, 242) : Color.FromArgb(230, 20, 20, 20);
                context.FillRectangle(labelCardBg, labelBgRect);
                if (_gridPen != null)
                {
                    context.DrawRectangle(_gridPen, labelBgRect);
                    context.DrawLine(_gridPen, labelX, bottomAreaY + 18, labelX + labelWidth, bottomAreaY + 18);
                    context.DrawLine(_gridPen, labelX, bottomAreaY + 36, labelX + labelWidth, bottomAreaY + 36);
                }

                // Draw Text labels
                var dSz = context.MeasureString("Delta", _font!);
                context.DrawString("Delta", _font!, StatsLabelColor, labelX + (labelWidth - (int)dSz.Width)/2, bottomAreaY + (18 - (int)dSz.Height)/2);

                var cSz = context.MeasureString("CD Day", _font!);
                context.DrawString("CD Day", _font!, StatsLabelColor, labelX + (labelWidth - (int)cSz.Width)/2, bottomAreaY + 18 + (18 - (int)cSz.Height)/2);

                var vSz = context.MeasureString("Candle Vol", _font!);
                context.DrawString("Candle Vol", _font!, StatsLabelColor, labelX + (labelWidth - (int)vSz.Width)/2, bottomAreaY + 36 + (18 - (int)vSz.Height)/2);
            }
        }

        // ----------------------------------------------------
        // Helper: Ticks Grouping (Price Consolidation) logic
        // ----------------------------------------------------
        private List<GroupedPriceLevel> GetGroupedPriceLevels(IndicatorCandle candle)
        {
            return GetGroupedPriceLevels(candle, TicksGrouping);
        }

        private List<GroupedPriceLevel> GetGroupedPriceLevels(IndicatorCandle candle, int ticksGrouping)
        {
            var rawLevels = candle.GetAllPriceLevels();

            decimal groupSize = this.InstrumentInfo!.TickSize * Math.Max(1, ticksGrouping);

            // Group the High and Low of the candle to align on the grid
            decimal lowGrouped = Math.Floor(candle.Low / groupSize) * groupSize;
            decimal highGrouped = Math.Floor(candle.High / groupSize) * groupSize;

            var groupedDict = new Dictionary<decimal, GroupedPriceLevel>();

            // Initialize all possible contiguous price levels in this candle's range
            for (decimal price = lowGrouped; price <= highGrouped; price += groupSize)
            {
                groupedDict[price] = new GroupedPriceLevel
                {
                    Price = price,
                    Volume = 0,
                    Ask = 0,
                    Bid = 0
                };
            }

            // Populate volume/ask/bid data from the raw candle data
            if (rawLevels != null)
            {
                foreach (var level in rawLevels)
                {
                    decimal groupedPrice = Math.Floor(level.Price / groupSize) * groupSize;

                    if (groupedDict.TryGetValue(groupedPrice, out var groupedLevel))
                    {
                        groupedLevel.Volume += level.Volume > 0 ? level.Volume : (level.Ask + level.Bid);
                        groupedLevel.Ask += level.Ask;
                        groupedLevel.Bid += level.Bid;
                    }
                }
            }

            return groupedDict.Values.OrderByDescending(l => l.Price).ToList();
        }

        // ----------------------------------------------------
        // Helper: Lazy initialization/update of rendering resources
        // ----------------------------------------------------
        private void UpdateResources()
        {
            if (_font == null || _lastFontFamily != FontFamily || _lastFontSize != FontSize)
            {
                _lastFontFamily = FontFamily;
                _lastFontSize = FontSize;
                _font = new RenderFont(FontFamily, FontSize);
            }

            if (_arrowFont == null || _lastFontFamily != FontFamily || _lastMajorArrowSize != MajorArrowSize)
            {
                _lastFontFamily = FontFamily;
                _lastMajorArrowSize = MajorArrowSize;
                _arrowFont = new RenderFont(FontFamily, MajorArrowSize);
            }

            if (_minorArrowFont == null || _lastFontFamily != FontFamily || _lastMinorArrowSize != MinorArrowSize)
            {
                _lastFontFamily = FontFamily;
                _lastMinorArrowSize = MinorArrowSize;
                _minorArrowFont = new RenderFont(FontFamily, MinorArrowSize);
            }

            if (_pocPen == null || _lastPocBorderColor != PocBorderColor || _lastPocBorderWidth != PocBorderWidth)
            {
                _lastPocBorderColor = PocBorderColor;
                _lastPocBorderWidth = PocBorderWidth;
                _pocPen = new RenderPen(PocBorderColor, PocBorderWidth);
            }

            if (_gridPen == null || _lastGridLineColor != GridLineColor)
            {
                _lastGridLineColor = GridLineColor;
                _gridPen = new RenderPen(GridLineColor, 1);
            }

            if (_bullWickPen == null || _lastBullColor != BullishCandleColor || _lastWickWidth != WickWidth)
            {
                _lastBullColor = BullishCandleColor;
                _lastWickWidth = WickWidth;
                _bullWickPen = new RenderPen(BullishCandleColor, WickWidth);
                _bullOutlinePen = new RenderPen(Color.FromArgb(255, BullishCandleColor), 1);
            }

            if (_bearWickPen == null || _lastBearColor != BearishCandleColor || _lastWickWidth != WickWidth)
            {
                _lastBearColor = BearishCandleColor;
                _lastWickWidth = WickWidth;
                _bearWickPen = new RenderPen(BearishCandleColor, WickWidth);
                _bearOutlinePen = new RenderPen(Color.FromArgb(255, BearishCandleColor), 1);
            }

            if (_buyImbalancePen == null || _lastBuyImbalanceColor != AskBidImbalanceColor || _lastImbalanceLineWidth != LineWidth)
            {
                _lastBuyImbalanceColor = AskBidImbalanceColor;
                _lastImbalanceLineWidth = LineWidth;
                _buyImbalancePen = new RenderPen(AskBidImbalanceColor, LineWidth);
            }

            if (_sellImbalancePen == null || _lastSellImbalanceColor != BidAskImbalanceColor || _lastImbalanceLineWidth != LineWidth)
            {
                _lastSellImbalanceColor = BidAskImbalanceColor;
                _lastImbalanceLineWidth = LineWidth;
                _sellImbalancePen = new RenderPen(BidAskImbalanceColor, LineWidth);
            }
        }

        // ----------------------------------------------------
        // Profile Management - Save Settings to Config File
        // ----------------------------------------------------
        private void SaveProfileSettings(IndicatorProfile profile)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "Indicators", "Profiles");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                string filepath = Path.Combine(folder, $"TotalVolDeltaFootprint_{profile}.cfg");
                
                var lines = new List<string>
                {
                    $"ProfileLabel={ProfileLabel}",
                    $"SessionResetHour={SessionResetHour}",
                    $"SessionResetMinute={SessionResetMinute}",
                    $"ColorTheme={ColorTheme}",
                    $"FontFamily={FontFamily}",
                    $"FontSize={FontSize}",
                    $"TicksGrouping={TicksGrouping}",
                    $"MinBarWidthForText={MinBarWidthForText}",
                    $"PurpleThreshold={PurpleThreshold}",
                    $"PurpleColor={PurpleColor.ToArgb()}",
                    $"OrangeThreshold={OrangeThreshold}",
                    $"OrangeColor={OrangeColor.ToArgb()}",
                    $"DefaultBgColor={DefaultBgColor.ToArgb()}",
                    $"VolumeTextColor={VolumeTextColor.ToArgb()}",
                    $"VolumeHighlightedTextColor={VolumeHighlightedTextColor.ToArgb()}",
                    $"PositiveDeltaColor={PositiveDeltaColor.ToArgb()}",
                    $"NegativeDeltaColor={NegativeDeltaColor.ToArgb()}",
                    $"NeutralDeltaColor={NeutralDeltaColor.ToArgb()}",
                    $"HighlightPoc={HighlightPoc}",
                    $"PocBorderColor={PocBorderColor.ToArgb()}",
                    $"PocBorderWidth={PocBorderWidth}",
                    $"DrawGridLines={DrawGridLines}",
                    $"GridLineColor={GridLineColor.ToArgb()}",
                    $"ShowCandleInMiddle={ShowCandleInMiddle}",
                    $"CandleWidth={CandleWidth}",
                    $"WickWidth={WickWidth}",
                    $"BullishCandleColor={BullishCandleColor.ToArgb()}",
                    $"BearishCandleColor={BearishCandleColor.ToArgb()}",
                    $"ShowRightProfile={ShowRightProfile}",
                    $"RightProfileWidth={RightProfileWidth}",
                    $"RightProfileBgColor={RightProfileBgColor.ToArgb()}",
                    $"RightProfileColorPositive={RightProfileColorPositive.ToArgb()}",
                    $"RightProfileColorNegative={RightProfileColorNegative.ToArgb()}",
                    $"ShowBottomStats={ShowBottomStats}",
                    $"DeltaPositiveBgColor={DeltaPositiveBgColor.ToArgb()}",
                    $"DeltaNegativeBgColor={DeltaNegativeBgColor.ToArgb()}",
                    $"CdDayPositiveBgColor={CdDayPositiveBgColor.ToArgb()}",
                    $"CdDayNegativeBgColor={CdDayNegativeBgColor.ToArgb()}",
                    $"CandleVolBgColor={CandleVolBgColor.ToArgb()}",
                    $"StatsTextColor={StatsTextColor.ToArgb()}",
                    $"StatsLabelColor={StatsLabelColor.ToArgb()}",
                    $"ShowImbalances={ShowImbalances}",
                    $"IgnoreZeroValues={IgnoreZeroValues}",
                    $"ImbalanceRatio={ImbalanceRatio}",
                    $"ImbalanceRange={ImbalanceRange}",
                    $"ImbalanceVolume={ImbalanceVolume}",
                    $"DaysLookBack={DaysLookBack}",
                    $"LineTillTouch={LineTillTouch}",
                    $"AskBidImbalanceColor={AskBidImbalanceColor.ToArgb()}",
                    $"BidAskImbalanceColor={BidAskImbalanceColor.ToArgb()}",
                    $"LineWidth={LineWidth}",
                    $"PrintLineForXBars={PrintLineForXBars}",
                    
                    $"ShowDivergence={ShowDivergence}",
                    $"DeltaPercentageThreshold={DeltaPercentageThreshold}",
                    $"ShowMinorDivergence={ShowMinorDivergence}",
                    $"MinorDeltaPercentageThreshold={MinorDeltaPercentageThreshold}",
                    $"MajorArrowSize={MajorArrowSize}",
                    $"MinorArrowSize={MinorArrowSize}",
                    $"BullishDivergenceColor={BullishDivergenceColor.ToArgb()}",
                    $"BearishDivergenceColor={BearishDivergenceColor.ToArgb()}",
                    $"MinorBullishDivergenceColor={MinorBullishDivergenceColor.ToArgb()}",
                    $"MinorBearishDivergenceColor={MinorBearishDivergenceColor.ToArgb()}",
                    $"MarkInvalidatedDivergences={MarkInvalidatedDivergences}",
                    $"InvalidationLookbackBars={InvalidationLookbackBars}",
                    $"InvalidatedArrowColor={InvalidatedArrowColor.ToArgb()}",
                    $"DivergenceDaysLookBack={DivergenceDaysLookBack}",
                    $"MaxDivergenceArrows={MaxDivergenceArrows}"
                };

                File.WriteAllLines(filepath, lines);
            }
            catch
            {
                // Suppress file access errors
            }
        }

        // ----------------------------------------------------
        // Profile Management - Load Settings from Config File
        // ----------------------------------------------------
        private void LoadProfileSettings(IndicatorProfile profile)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ATAS", "Indicators", "Profiles");
                string filepath = Path.Combine(folder, $"TotalVolDeltaFootprint_{profile}.cfg");

                if (!File.Exists(filepath))
                {
                    ApplyBuiltInProfileDefaults(profile);
                    SaveProfileSettings(profile);
                    return;
                }

                var lines = File.ReadAllLines(filepath);
                var dict = new Dictionary<string, string>();
                foreach (var line in lines)
                {
                    int idx = line.IndexOf('=');
                    if (idx > 0)
                    {
                        string k = line.Substring(0, idx).Trim();
                        string v = line.Substring(idx + 1).Trim();
                        dict[k] = v;
                    }
                }

                _isApplyingProfile = true;

                var defaultSessionReset = GetDefaultSessionReset(profile);
                _sessionResetHour = defaultSessionReset.Hour;
                _sessionResetMinute = defaultSessionReset.Minute;

                if (IsBuiltInTradingProfile(profile))
                    _profileLabel = GetDefaultProfileLabel(profile);
                else if (dict.TryGetValue("ProfileLabel", out string? profileLabel))
                    _profileLabel = profileLabel;
                else
                    _profileLabel = GetDefaultProfileLabel(profile);

                _profileLabelsMap[profile] = _profileLabel;

                if (dict.TryGetValue("SessionResetHour", out string? resetHourStr) && int.TryParse(resetHourStr, out int resetHour)) _sessionResetHour = Math.Clamp(resetHour, 0, 23);
                if (dict.TryGetValue("SessionResetMinute", out string? resetMinuteStr) && int.TryParse(resetMinuteStr, out int resetMinute)) _sessionResetMinute = Math.Clamp(resetMinute, 0, 59);

                if (dict.TryGetValue("ColorTheme", out string? colorThemeStr) && Enum.TryParse(colorThemeStr, out ColorThemeMode themeMode)) _colorTheme = themeMode;

                if (dict.TryGetValue("FontFamily", out string? fontFamily)) _fontFamily = fontFamily;
                if (dict.TryGetValue("FontSize", out string? fontSizeStr) && int.TryParse(fontSizeStr, out int fontSize)) _fontSize = fontSize;
                if (dict.TryGetValue("TicksGrouping", out string? ticksGroupingStr) && int.TryParse(ticksGroupingStr, out int ticksGrouping)) _ticksGrouping = ticksGrouping;
                if (dict.TryGetValue("MinBarWidthForText", out string? minBarWidthForTextStr) && int.TryParse(minBarWidthForTextStr, out int minBarWidthForText)) _minBarWidthForText = minBarWidthForText;
                if (dict.TryGetValue("PurpleThreshold", out string? purpleThresholdStr) && decimal.TryParse(purpleThresholdStr, out decimal purpleThreshold)) _purpleThreshold = purpleThreshold;
                if (dict.TryGetValue("PurpleColor", out string? purpleColorStr) && int.TryParse(purpleColorStr, out int purpleColorArgb)) _purpleColor = Color.FromArgb(purpleColorArgb);
                if (dict.TryGetValue("OrangeThreshold", out string? orangeThresholdStr) && decimal.TryParse(orangeThresholdStr, out decimal orangeThreshold)) _orangeThreshold = orangeThreshold;
                if (dict.TryGetValue("OrangeColor", out string? orangeColorStr) && int.TryParse(orangeColorStr, out int orangeColorArgb)) _orangeColor = Color.FromArgb(orangeColorArgb);
                if (dict.TryGetValue("DefaultBgColor", out string? defaultBgColorStr) && int.TryParse(defaultBgColorStr, out int defaultBgColorArgb)) _defaultBgColor = Color.FromArgb(defaultBgColorArgb);
                if (dict.TryGetValue("VolumeTextColor", out string? volumeTextColorStr) && int.TryParse(volumeTextColorStr, out int volumeTextColorArgb)) _volumeTextColor = Color.FromArgb(volumeTextColorArgb);
                if (dict.TryGetValue("VolumeHighlightedTextColor", out string? volumeHighlightedTextColorStr) && int.TryParse(volumeHighlightedTextColorStr, out int volumeHighlightedTextColorArgb)) _volumeHighlightedTextColor = Color.FromArgb(volumeHighlightedTextColorArgb);
                if (dict.TryGetValue("PositiveDeltaColor", out string? positiveDeltaColorStr) && int.TryParse(positiveDeltaColorStr, out int positiveDeltaColorArgb)) _positiveDeltaColor = Color.FromArgb(positiveDeltaColorArgb);
                if (dict.TryGetValue("NegativeDeltaColor", out string? negativeDeltaColorStr) && int.TryParse(negativeDeltaColorStr, out int negativeDeltaColorArgb)) _negativeDeltaColor = Color.FromArgb(negativeDeltaColorArgb);
                if (dict.TryGetValue("NeutralDeltaColor", out string? neutralDeltaColorStr) && int.TryParse(neutralDeltaColorStr, out int neutralDeltaColorArgb)) _neutralDeltaColor = Color.FromArgb(neutralDeltaColorArgb);
                if (dict.TryGetValue("HighlightPoc", out string? highlightPocStr) && bool.TryParse(highlightPocStr, out bool highlightPoc)) _highlightPoc = highlightPoc;
                if (dict.TryGetValue("PocBorderColor", out string? pocBorderColorStr) && int.TryParse(pocBorderColorStr, out int pocBorderColorArgb)) _pocBorderColor = Color.FromArgb(pocBorderColorArgb);
                if (dict.TryGetValue("PocBorderWidth", out string? pocBorderWidthStr) && int.TryParse(pocBorderWidthStr, out int pocBorderWidth)) _pocBorderWidth = pocBorderWidth;
                if (dict.TryGetValue("DrawGridLines", out string? drawGridLinesStr) && bool.TryParse(drawGridLinesStr, out bool drawGridLines)) _drawGridLines = drawGridLines;
                if (dict.TryGetValue("GridLineColor", out string? gridLineColorStr) && int.TryParse(gridLineColorStr, out int gridLineColorArgb)) _gridLineColor = Color.FromArgb(gridLineColorArgb);
                
                if (dict.TryGetValue("ShowCandleInMiddle", out string? showCandleInMiddleStr) && bool.TryParse(showCandleInMiddleStr, out bool showCandleInMiddle)) _showCandleInMiddle = showCandleInMiddle;
                if (dict.TryGetValue("CandleWidth", out string? candleWidthStr) && int.TryParse(candleWidthStr, out int candleWidth)) _candleWidth = candleWidth;
                if (dict.TryGetValue("WickWidth", out string? wickWidthStr) && int.TryParse(wickWidthStr, out int wickWidth)) _wickWidth = wickWidth;
                if (dict.TryGetValue("BullishCandleColor", out string? bullishCandleColorStr) && int.TryParse(bullishCandleColorStr, out int bullishCandleColorArgb)) _bullishCandleColor = Color.FromArgb(bullishCandleColorArgb);
                if (dict.TryGetValue("BearishCandleColor", out string? bearishCandleColorStr) && int.TryParse(bearishCandleColorStr, out int bearishCandleColorArgb)) _bearishCandleColor = Color.FromArgb(bearishCandleColorArgb);
                
                if (dict.TryGetValue("ShowRightProfile", out string? showRightProfileStr) && bool.TryParse(showRightProfileStr, out bool showRightProfile)) _showRightProfile = showRightProfile;
                if (dict.TryGetValue("RightProfileWidth", out string? rightProfileWidthStr) && int.TryParse(rightProfileWidthStr, out int rightProfileWidth)) _rightProfileWidth = rightProfileWidth;
                if (dict.TryGetValue("RightProfileBgColor", out string? rightProfileBgColorStr) && int.TryParse(rightProfileBgColorStr, out int rightProfileBgColorArgb)) _rightProfileBgColor = Color.FromArgb(rightProfileBgColorArgb);
                if (dict.TryGetValue("RightProfileColorPositive", out string? rightProfileColorPositiveStr) && int.TryParse(rightProfileColorPositiveStr, out int rightProfileColorPositiveArgb)) _rightProfileColorPositive = Color.FromArgb(rightProfileColorPositiveArgb);
                if (dict.TryGetValue("RightProfileColorNegative", out string? rightProfileColorNegativeStr) && int.TryParse(rightProfileColorNegativeStr, out int rightProfileColorNegativeArgb)) _rightProfileColorNegative = Color.FromArgb(rightProfileColorNegativeArgb);
                
                if (dict.TryGetValue("ShowBottomStats", out string? showBottomStatsStr) && bool.TryParse(showBottomStatsStr, out bool showBottomStats)) _showBottomStats = showBottomStats;
                if (dict.TryGetValue("DeltaPositiveBgColor", out string? deltaPositiveBgColorStr) && int.TryParse(deltaPositiveBgColorStr, out int deltaPositiveBgColorArgb)) _deltaPositiveBgColor = Color.FromArgb(deltaPositiveBgColorArgb);
                if (dict.TryGetValue("DeltaNegativeBgColor", out string? deltaNegativeBgColorStr) && int.TryParse(deltaNegativeBgColorStr, out int deltaNegativeBgColorArgb)) _deltaNegativeBgColor = Color.FromArgb(deltaNegativeBgColorArgb);
                if (dict.TryGetValue("CdDayPositiveBgColor", out string? cdDayPositiveBgColorStr) && int.TryParse(cdDayPositiveBgColorStr, out int cdDayPositiveBgColorArgb)) _cdDayPositiveBgColor = Color.FromArgb(cdDayPositiveBgColorArgb);
                if (dict.TryGetValue("CdDayNegativeBgColor", out string? cdDayNegativeBgColorStr) && int.TryParse(cdDayNegativeBgColorStr, out int cdDayNegativeBgColorArgb)) _cdDayNegativeBgColor = Color.FromArgb(cdDayNegativeBgColorArgb);
                if (dict.TryGetValue("CandleVolBgColor", out string? candleVolBgColorStr) && int.TryParse(candleVolBgColorStr, out int candleVolBgColorArgb)) _candleVolBgColor = Color.FromArgb(candleVolBgColorArgb);
                if (dict.TryGetValue("StatsTextColor", out string? statsTextColorStr) && int.TryParse(statsTextColorStr, out int statsTextColorArgb)) _statsTextColor = Color.FromArgb(statsTextColorArgb);
                if (dict.TryGetValue("StatsLabelColor", out string? statsLabelColorStr) && int.TryParse(statsLabelColorStr, out int statsLabelColorArgb)) _statsLabelColor = Color.FromArgb(statsLabelColorArgb);

                if (dict.TryGetValue("ShowImbalances", out string? showImbalancesStr) && bool.TryParse(showImbalancesStr, out bool showImbalances)) _showImbalances = showImbalances;
                if (dict.TryGetValue("IgnoreZeroValues", out string? ignoreZeroValuesStr) && bool.TryParse(ignoreZeroValuesStr, out bool ignoreZeroValues)) _ignoreZeroValues = ignoreZeroValues;
                if (dict.TryGetValue("ImbalanceRatio", out string? imbalanceRatioStr) && decimal.TryParse(imbalanceRatioStr, out decimal imbalanceRatio)) _imbalanceRatio = imbalanceRatio;
                if (dict.TryGetValue("ImbalanceRange", out string? imbalanceRangeStr) && int.TryParse(imbalanceRangeStr, out int imbalanceRange)) _imbalanceRange = imbalanceRange;
                if (dict.TryGetValue("ImbalanceVolume", out string? imbalanceVolumeStr) && decimal.TryParse(imbalanceVolumeStr, out decimal imbalanceVolume)) _imbalanceVolume = imbalanceVolume;
                if (dict.TryGetValue("DaysLookBack", out string? daysLookBackStr) && int.TryParse(daysLookBackStr, out int daysLookBack)) _daysLookBack = daysLookBack;
                if (dict.TryGetValue("LineTillTouch", out string? lineTillTouchStr) && bool.TryParse(lineTillTouchStr, out bool lineTillTouch)) _lineTillTouch = lineTillTouch;
                if (dict.TryGetValue("AskBidImbalanceColor", out string? askBidImbalanceColorStr) && int.TryParse(askBidImbalanceColorStr, out int askBidImbalanceColorArgb)) _askBidImbalanceColor = Color.FromArgb(askBidImbalanceColorArgb);
                if (dict.TryGetValue("BidAskImbalanceColor", out string? bidAskImbalanceColorStr) && int.TryParse(bidAskImbalanceColorStr, out int bidAskImbalanceColorArgb)) _bidAskImbalanceColor = Color.FromArgb(bidAskImbalanceColorArgb);
                if (dict.TryGetValue("LineWidth", out string? lineWidthStr) && int.TryParse(lineWidthStr, out int lineWidth)) _lineWidth = lineWidth;
                if (dict.TryGetValue("PrintLineForXBars", out string? printLineForXBarsStr) && int.TryParse(printLineForXBarsStr, out int printLineForXBars)) _printLineForXBars = printLineForXBars;

                if (dict.TryGetValue("ShowDivergence", out string? showDivergenceStr) && bool.TryParse(showDivergenceStr, out bool showDivergence)) _showDivergence = showDivergence;
                if (dict.TryGetValue("DeltaPercentageThreshold", out string? deltaPercentageThresholdStr) && decimal.TryParse(deltaPercentageThresholdStr, out decimal deltaPercentageThreshold)) _deltaPercentageThreshold = deltaPercentageThreshold;
                if (dict.TryGetValue("ShowMinorDivergence", out string? showMinorDivergenceStr) && bool.TryParse(showMinorDivergenceStr, out bool showMinorDivergence)) _showMinorDivergence = showMinorDivergence;
                if (dict.TryGetValue("MinorDeltaPercentageThreshold", out string? minorDeltaPercentageThresholdStr) && decimal.TryParse(minorDeltaPercentageThresholdStr, out decimal minorDeltaPercentageThreshold)) _minorDeltaPercentageThreshold = minorDeltaPercentageThreshold;
                if (dict.TryGetValue("MajorArrowSize", out string? majorArrowSizeStr) && int.TryParse(majorArrowSizeStr, out int majorArrowSize)) _majorArrowSize = majorArrowSize;
                if (dict.TryGetValue("MinorArrowSize", out string? minorArrowSizeStr) && int.TryParse(minorArrowSizeStr, out int minorArrowSize)) _minorArrowSize = minorArrowSize;
                if (dict.TryGetValue("BullishDivergenceColor", out string? bullishDivergenceColorStr) && int.TryParse(bullishDivergenceColorStr, out int bullishDivergenceColorArgb)) _bullishDivergenceColor = Color.FromArgb(bullishDivergenceColorArgb);
                if (dict.TryGetValue("BearishDivergenceColor", out string? bearishDivergenceColorStr) && int.TryParse(bearishDivergenceColorStr, out int bearishDivergenceColorArgb)) _bearishDivergenceColor = Color.FromArgb(bearishDivergenceColorArgb);
                if (dict.TryGetValue("MinorBullishDivergenceColor", out string? minorBullishDivergenceColorStr) && int.TryParse(minorBullishDivergenceColorStr, out int minorBullishDivergenceColorArgb)) _minorBullishDivergenceColor = Color.FromArgb(minorBullishDivergenceColorArgb);
                if (dict.TryGetValue("MinorBearishDivergenceColor", out string? minorBearishDivergenceColorStr) && int.TryParse(minorBearishDivergenceColorStr, out int minorBearishDivergenceColorArgb)) _minorBearishDivergenceColor = Color.FromArgb(minorBearishDivergenceColorArgb);
                if (dict.TryGetValue("MarkInvalidatedDivergences", out string? markInvalidatedDivergencesStr) && bool.TryParse(markInvalidatedDivergencesStr, out bool markInvalidatedDivergences)) _markInvalidatedDivergences = markInvalidatedDivergences;
                if (dict.TryGetValue("InvalidationLookbackBars", out string? invalidationLookbackBarsStr) && int.TryParse(invalidationLookbackBarsStr, out int invalidationLookbackBars)) _invalidationLookbackBars = invalidationLookbackBars;
                if (dict.TryGetValue("InvalidatedArrowColor", out string? invalidatedArrowColorStr) && int.TryParse(invalidatedArrowColorStr, out int invalidatedArrowColorArgb)) _invalidatedArrowColor = Color.FromArgb(invalidatedArrowColorArgb);
                if (dict.TryGetValue("DivergenceDaysLookBack", out string? divergenceDaysLookBackStr) && int.TryParse(divergenceDaysLookBackStr, out int divergenceDaysLookBack)) _divergenceDaysLookBack = divergenceDaysLookBack;
                if (dict.TryGetValue("MaxDivergenceArrows", out string? maxDivergenceArrowsStr) && int.TryParse(maxDivergenceArrowsStr, out int maxDivergenceArrows)) _maxDivergenceArrows = maxDivergenceArrows;

                // Preserve semantic ordering even when an older or hand-edited
                // profile contains inconsistent threshold values.
                if (_orangeThreshold < _purpleThreshold) _orangeThreshold = _purpleThreshold;
                if (_minorDeltaPercentageThreshold > _deltaPercentageThreshold) _minorDeltaPercentageThreshold = _deltaPercentageThreshold;

                _isApplyingProfile = false;
            }
            catch
            {
                _isApplyingProfile = false;
            }
        }
    }
}
