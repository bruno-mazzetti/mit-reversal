# MIT Reversal for NinjaTrader 8

An RSI and engulfing candle reversal indicator with optional intrabar price confirmation. It draws chart markers and exposes a numeric `Signal` plot for NinjaScript and plot readers. It does not place orders.

**Status:** experimental indicator. The source was extracted from the user's NinjaTrader installation; NinjaTrader-generated cache wrappers were removed because the editor recreates them. This repository does not claim a profitable trading strategy or verified live automation.

## Install

Download [`MITReversal_NT8_v1.0.0.zip`](MITReversal_NT8_v1.0.0.zip) and select **Tools > Import > NinjaScript Add-On** in NinjaTrader. Keep the ZIP intact. Alternatively, copy [`MITReversal.cs`](MITReversal.cs) into `Documents\NinjaTrader 8\bin\Custom\Indicators`, compile in NinjaScript Editor (F5), and add **MITReversal** to a chart. The ZIP layout and source match were checked; the import itself has not been tested.

## Logic

- Wilder RSI is calculated from completed bars. A bullish candidate requires a bearish previous candle followed by a candle closing at or above that candle's open, with RSI at or below the oversold threshold within the configured lookback. Bearish logic is symmetric.
- Minimum and maximum candle ranges can filter candidates. A zero limit disables that side of the range filter.
- With **Use MIT Confirmation** enabled, the candidate arms a setup. A later last-traded price must touch the candidate high plus the tick offset for a long, or the low minus the offset for a short. A strict break of the opposite side invalidates it. Pending setups expire after the configured number of bars or at a new session.
- With confirmation disabled, the indicator emits the candidate signal after its candle closes.
- The transparent `Signal` plot is `+1` for long, `-1` for short and `0` otherwise. A signal remains on its event bar and resets on the next bar.

## Parameters

| Setting | Default | Purpose |
|---|---:|---|
| RSI Length | 14 | Wilder RSI period |
| RSI Overbought / Oversold | 70 / 30 | Inclusive candidate thresholds |
| RSI Lookback | 2 | Previous candles checked in addition to the candidate |
| Use MIT Confirmation | true | Require a subsequent intrabar price touch |
| Confirmation Ticks | 4 | Distance beyond the candidate extreme |
| Cancel After Bars | 2 | Complete subsequent bars allowed |
| Min / Max Candle Range | 0 / 0 | Optional range limits in ticks |
| Show Armed Markers | true | Display pending setup dots |

## Historical data and automation

This indicator uses `Calculate.OnPriceChange`. Without **Tick Replay**, NinjaTrader's historical OHLC bars cannot establish the order of a trigger and invalidation touch; the code deliberately suppresses approximate historical signals in that mode. Validate intrabar behavior with Tick Replay and Playback or Market Replay before relying on it. The code has no TradeSaber dependency; third-party order execution has not been verified.

The standalone source compiled against locally installed NinjaTrader 8.1.7.1 assemblies with compiler warnings treated as errors. This does not validate chart rendering or signal behavior in the native application.

## Project context and credit

The user specified and iterated the indicator behavior; the NT8 adaptation and documentation were produced with AI assistance. The source credits the original **EngulfingCandle** Pine logic to **ahmedirshad419** and retains its Mozilla Public License 2.0 header. The adaptation adds NT8 integration, RSI and price confirmation controls. See the [MPL 2.0 license](https://www.mozilla.org/en-US/MPL/2.0/). NinjaTrader is a third-party product; this project is not affiliated with or endorsed by it.
