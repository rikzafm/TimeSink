import SwiftUI
import TimeSinkCore

/// The Today strip: 24 hour bars, a now flag, and the typical day ghosted ahead of now.
public struct HourlyChartView: View {
    @ObservedObject var state: AppState
    public let result: TodayResult

    public init(state: AppState, result: TodayResult) {
        self.state = state
        self.result = result
    }

    private let barsHeight: CGFloat = 46
    private var split: Bool { state.showsDeviceSplit }
    private var isLive: Bool { state.daysBack == 0 }
    private var nowFraction: CGFloat {
        CGFloat(result.nowUtc - result.dayStartUtc) / CGFloat(max(1, result.dayEndUtc - result.dayStartUtc))
    }
    private var nowHour: Int { isLive ? min(23, Int(nowFraction * 24)) : 24 }
    private var scale: Double {
        let top = result.hours.map { max(Double($0.appSeconds), $0.typicalAppSeconds) }.max() ?? 0
        return max(3600, top)
    }

    private func isAsleep(_ hour: Int) -> Bool {
        let start = result.dayStartUtc + Int64(hour) * 3600
        let end = start + 3600
        return result.sleepBands.contains { min($0.endUtc, end) - max($0.startUtc, start) >= 1800 }
    }

    private func height(_ seconds: Double) -> CGFloat {
        CGFloat(seconds / scale) * barsHeight
    }

    private func color(_ seconds: Int64, hour: Int) -> Color {
        if hour == nowHour { return ThemeColors.accent }
        let f = Double(seconds) / scale
        if f <= 0 { return Tokens.ramp[0] }
        return f > 0.75 ? Tokens.ramp[3] : f > 0.5 ? Tokens.ramp[2] : Tokens.ramp[1]
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ZStack(alignment: .topLeading) {
                VStack(spacing: 0) {
                    Spacer(minLength: 0)
                    bars.frame(height: barsHeight)
                    axis.frame(height: 16)
                }
                if isLive { nowFlag }
            }
            .frame(height: 78)

            legend
        }
    }

    private var bars: some View {
        HStack(alignment: .bottom, spacing: 3) {
            ForEach(result.hours, id: \.hour) { bucket in
                barView(bucket)
                    .frame(maxWidth: .infinity)
                    .frame(height: barsHeight, alignment: .bottom)
            }
        }
    }

    @ViewBuilder
    private func barView(_ bucket: HourBucket) -> some View {
        if bucket.hour > nowHour {
            RoundedRectangle(cornerRadius: 2)
                .strokeBorder(Tokens.typical, style: StrokeStyle(lineWidth: 1, dash: [1, 1.5]))
                .frame(height: max(2, height(bucket.typicalAppSeconds)))
                .opacity(result.hasTypical ? 1 : 0)
        } else if isAsleep(bucket.hour) {
            Hatch(color: Tokens.ramp[2])
                .frame(height: max(height(Double(bucket.appSeconds)), barsHeight * 0.7))
                .clipShape(RoundedRectangle(cornerRadius: 2))
        } else if split {
            let parts = state.deviceSplit(bucket.byDevice?.mapValues(\.appSeconds))
            OverlapBar(
                mac: height(Double(parts.mac)),
                pc: height(Double(parts.pc)),
                macColor: bucket.hour == nowHour ? ThemeColors.accent : Tokens.ramp[3],
                pcColor: ThemeColors.deviceBDark,
                radius: 2
            )
        } else {
            RoundedRectangle(cornerRadius: 2)
                .fill(color(bucket.appSeconds, hour: bucket.hour))
                .frame(height: max(2, height(Double(bucket.appSeconds))))
        }
    }

    private var axis: some View {
        HStack {
            ForEach(Array(["12 am", "6 am", "12 pm", "6 pm", "12 am"].enumerated()), id: \.offset) { i, label in
                if i > 0 { Spacer() }
                Text(label)
            }
        }
        .font(.system(size: 10))
        .foregroundColor(ThemeColors.textMuted)
        .frame(maxHeight: .infinity, alignment: .bottom)
    }

    private var nowFlag: some View {
        GeometryReader { geo in
            let x = nowFraction * geo.size.width
            Rectangle().fill(ThemeColors.accent).frame(width: 1, height: 62).offset(x: x)
            Text(Formatters.clock12(result.nowUtc))
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(ThemeColors.background)
                .padding(.horizontal, 6)
                .frame(height: 16)
                .background(RoundedRectangle(cornerRadius: 4).fill(ThemeColors.accent))
                .fixedSize()
                .offset(x: min(x - 24, geo.size.width - 52))
        }
    }

    private func swatch(_ color: Color) -> some View {
        RoundedRectangle(cornerRadius: 2).fill(color).frame(width: 8, height: 8)
    }

    private var typicalKey: some View {
        HStack(spacing: 5) {
            Rectangle()
                .stroke(ThemeColors.textMuted, style: StrokeStyle(lineWidth: 2, dash: [2, 2]))
                .frame(width: 14, height: 0.5)
            Text("Typical day")
        }
    }

    private func asleepKey(_ label: String) -> some View {
        HStack(spacing: 5) {
            Hatch(color: ThemeColors.textMuted, line: 1.5, gap: 1.5)
                .frame(width: 8, height: 8)
                .clipShape(RoundedRectangle(cornerRadius: 2))
            Text(label)
        }
    }

    private var legend: some View {
        HStack(spacing: split ? 9 : 14) {
            if split {
                HStack(spacing: 5) { swatch(Tokens.ramp[3]); Text("Mac on") }
                HStack(spacing: 5) { swatch(ThemeColors.deviceBDark); Text("PC on") }
                HStack(spacing: 5) {
                    ZStack(alignment: .bottomLeading) {
                        RoundedRectangle(cornerRadius: 1).fill(Tokens.ramp[3]).frame(width: 7, height: 8)
                        RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceBDark).frame(width: 7, height: 5).offset(x: 5)
                    }
                    .frame(width: 12, height: 8, alignment: .leading)
                    Text("Overlapping = both on")
                }
                typicalKey
                asleepKey("Asleep")
            } else {
                HStack(spacing: 5) { swatch(ThemeColors.accent); Text("Apps open") }
                typicalKey
                asleepKey("Mac asleep · paused")
            }
        }
        .font(.system(size: 10))
        .foregroundColor(ThemeColors.textMuted)
        .lineLimit(1)
    }
}
