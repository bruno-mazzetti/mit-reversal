// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. https://mozilla.org/MPL/2.0/
// Original EngulfingCandle Pine logic: © ahmedirshad419.
// NT8 adaptation and optional price confirmation, September 2026.
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MITReversal : Indicator
    {
        private Series<double> closedRsi;
        private double gain, loss;
        private int rsiSamples, lastRsiBar = -1, lastSeenBar = -1;
        private int direction, armedBar, firedBar = -1;
        private double trigger, invalidation;
        private bool warned;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MITReversal";
                Description = "Closed-candle RSI reversal with optional intrabar confirmation. Signal: +1 long, -1 short, 0 idle. No orders are submitted.";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;
                BarsRequiredToPlot = 1;
                RSILength = 14;
                RSIOverbought = 70;
                RSIOversold = 30;
                RSILookback = 2;
                UseConfirmation = true;
                ConfirmationTicks = 4;
                CancelAfterBars = 2;
                MinCandleRangeTicks = 0;
                MaxCandleRangeTicks = 0;
                ShowArmedMarkers = true;
                AddPlot(Brushes.Transparent, "Signal");
            }
            else if (State == State.Configure)
            {
                Calculate = Calculate.OnPriceChange;
                if (RSIOversold >= RSIOverbought)
                    throw new ArgumentException("RSI Oversold must be lower than RSI Overbought.");
                if (MaxCandleRangeTicks > 0 && MinCandleRangeTicks > MaxCandleRangeTicks)
                    throw new ArgumentException("Min Candle Range cannot exceed Max Candle Range.");
            }
            else if (State == State.DataLoaded)
            {
                closedRsi = new Series<double>(this, MaximumBarsLookBack.Infinite);
            }
        }

        // Wilder RSI: SMA seed over RSILength changes, then Wilder smoothing.
        // Calculated once per CLOSED bar; Input defaults to Close.
        private void UpdateClosedRsi(int barsAgo)
        {
            int bar = CurrentBar - barsAgo;
            if (bar <= lastRsiBar) return;
            lastRsiBar = bar;
            closedRsi[barsAgo] = double.NaN;
            if (bar == 0) return;
            double delta = Input[barsAgo] - Input[barsAgo + 1];
            double up = Math.Max(delta, 0), down = Math.Max(-delta, 0);
            if (rsiSamples < RSILength)
            {
                gain += up; loss += down; rsiSamples++;
                if (rsiSamples < RSILength) return;
                gain /= RSILength; loss /= RSILength;
            }
            else
            {
                gain = (gain * (RSILength - 1) + up) / RSILength;
                loss = (loss * (RSILength - 1) + down) / RSILength;
            }
            closedRsi[barsAgo] = loss == 0 ? (gain == 0 ? 50 : 100)
                : 100 - 100 / (1 + gain / loss);
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            bool newBar = CurrentBar != lastSeenBar;
            if (newBar)
            {
                lastSeenBar = CurrentBar;
                Signal[0] = 0;
                firedBar = -1;
            }

            // OHLC alone cannot establish which boundary was hit first.
            // Warm up RSI, but deliberately emit no approximate historical trades.
            if (State == State.Historical && !Bars.IsTickReplay)
            {
                UpdateClosedRsi(0);
                direction = 0;
                if (!warned)
                {
                    Print(Name + ": Historical signals require Tick Replay. Use Playback/Market Replay for intrabar validation.");
                    warned = true;
                }
                return;
            }

            if (newBar)
            {
                if (CurrentBar > 0) UpdateClosedRsi(1);
                bool newSession = Bars.IsFirstBarOfSession;
                if (newSession) direction = 0;
                // N means N entire subsequent bars, excluding the signal candle.
                if (direction != 0 && CurrentBar - armedBar > CancelAfterBars)
                    direction = 0;

                // No replacement, extension or opposite rearming while a setup is active.
                if (!newSession && direction == 0 && CurrentBar >= RSILength + RSILookback + 1)
                {
                    double range = (High[1] - Low[1]) / TickSize;
                    bool validRange = range + 1e-8 >= MinCandleRangeTicks
                        && (MaxCandleRangeTicks == 0 || range <= MaxCandleRangeTicks + 1e-8);
                    bool oversold = false, overbought = false;
                    for (int i = 0; i <= RSILookback; i++)
                    {
                        double value = closedRsi[1 + i];
                        if (double.IsNaN(value)) continue;
                        oversold |= value <= RSIOversold;
                        overbought |= value >= RSIOverbought;
                    }
                    bool bullish = Close[1] >= Open[2] && Close[2] < Open[2];
                    bool bearish = Close[1] <= Open[2] && Close[2] > Open[2];
                    int candidate = validRange ? (bullish && oversold ? 1 : bearish && overbought ? -1 : 0) : 0;
                    if (candidate != 0)
                    {
                        if (!UseConfirmation) Fire(candidate);
                        else
                        {
                            direction = candidate;
                            armedBar = CurrentBar - 1;
                            trigger = Instrument.MasterInstrument.RoundToTickSize(candidate > 0
                                ? High[1] + ConfirmationTicks * TickSize
                                : Low[1] - ConfirmationTicks * TickSize);
                            invalidation = candidate > 0 ? Low[1] : High[1];
                            if (ShowArmedMarkers)
                                Draw.Dot(this, "MITArm" + armedBar, false, 1,
                                    candidate > 0 ? Low[1] - 2 * TickSize : High[1] + 2 * TickSize,
                                    Brushes.Goldenrod);
                        }
                    }
                }
            }

            // Only the current last-traded price is used: never a cumulative bar high/low.
            // Invalidation is a strict break; a retest at exactly the boundary is allowed.
            if (direction != 0)
            {
                double price = Close[0];
                bool invalid = direction > 0 ? price < invalidation : price > invalidation;
                bool confirmed = direction > 0 ? price >= trigger : price <= trigger;
                if (invalid) direction = 0;
                else if (confirmed)
                {
                    int confirmedDirection = direction;
                    direction = 0;
                    Fire(confirmedDirection);
                }
            }
        }

        private void Fire(int side)
        {
            if (firedBar == CurrentBar) return;
            firedBar = CurrentBar;
            // Latched through the trigger bar for consumers; reset on the next bar.
            Signal[0] = side;
            if (side > 0)
                Draw.ArrowUp(this, "MITLong" + CurrentBar, false, 0, Low[0] - 3 * TickSize, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, "MITShort" + CurrentBar, false, 0, High[0] + 3 * TickSize, Brushes.Red);
        }

        [Browsable(false), XmlIgnore]
        public Series<double> Signal { get { return Values[0]; } }

        [NinjaScriptProperty]
        [Range(2, 1000)]
        [Display(Name = "RSI Length", Description = "Wilder RSI period.", GroupName = "RSI", Order = 1)]
        public int RSILength { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "RSI Overbought", Description = "Overbought threshold, inclusive.", GroupName = "RSI", Order = 2)]
        public double RSIOverbought { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "RSI Oversold", Description = "Oversold threshold, inclusive.", GroupName = "RSI", Order = 3)]
        public double RSIOversold { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "RSI Lookback", Description = "Extra previous candles checked; 2 means signal candle plus two previous candles.", GroupName = "RSI", Order = 4)]
        public int RSILookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use MIT Confirmation", Description = "False emits the original signal after the MIT candle closes.", GroupName = "Confirmation", Order = 1)]
        public bool UseConfirmation { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Confirmation Ticks", Description = "Ticks beyond the signal high for long or low for short. Zero still requires a touch when confirmation is enabled.", GroupName = "Confirmation", Order = 2)]
        public int ConfirmationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Cancel After Bars", Description = "Number of complete subsequent bars allowed for confirmation.", GroupName = "Confirmation", Order = 3)]
        public int CancelAfterBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Min Candle Range (ticks)", Description = "Minimum total high-low range. Zero disables the lower filter.", GroupName = "Candle Range", Order = 1)]
        public int MinCandleRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max Candle Range (ticks)", Description = "Maximum total high-low range. Zero disables the upper filter.", GroupName = "Candle Range", Order = 2)]
        public int MaxCandleRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Armed Markers", Description = "Golden dots mark armed candles; arrows mark confirmed signals.", GroupName = "Visual", Order = 1)]
        public bool ShowArmedMarkers { get; set; }
    }
}
