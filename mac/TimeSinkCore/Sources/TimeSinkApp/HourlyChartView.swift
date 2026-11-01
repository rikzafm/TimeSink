import SwiftUI
import TimeSinkCore

public struct HourlyChartView: View {
    public let hours: [HourBucket]
    public let isAllScope: Bool
    public let height: CGFloat = 80

    public init(hours: [HourBucket], isAllScope: Bool) {
        self.hours = hours
        self.isAllScope = isAllScope
    }

    public var body: some View {
        VStack(spacing: 4) {
            GeometryReader { geo in
                let maxSeconds = max(3600, hours.map(\.appSeconds).max() ?? 3600)
                let columnWidth = (geo.size.width - CGFloat(hours.count - 1) * 2) / CGFloat(hours.count)

                HStack(alignment: .bottom, spacing: 2) {
                    ForEach(hours, id: \.hour) { bucket in
                        let barHeight = CGFloat(bucket.appSeconds) / CGFloat(maxSeconds) * height

                        if isAllScope, let dev = bucket.byDevice, dev.count > 1 {
                            // Overlapping bars: Device A (Mac) and Device B (PC)
                            let devA = dev.values.first?.appSeconds ?? 0
                            let devB = dev.values.dropFirst().first?.appSeconds ?? 0
                            let hA = CGFloat(devA) / CGFloat(maxSeconds) * height
                            let hB = CGFloat(devB) / CGFloat(maxSeconds) * height

                            ZStack(alignment: .bottomLeading) {
                                // Background column track
                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.barBackground)
                                    .frame(width: columnWidth, height: height)

                                if hA >= hB {
                                    // Device A behind, Device B in front
                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceA)
                                        .frame(width: columnWidth * 0.7, height: max(2, hA))

                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceB)
                                        .frame(width: columnWidth * 0.7, height: max(2, hB))
                                        .offset(x: columnWidth * 0.3)
                                } else {
                                    // Device B behind, Device A in front
                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceB)
                                        .frame(width: columnWidth * 0.7, height: max(2, hB))
                                        .offset(x: columnWidth * 0.3)

                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceA)
                                        .frame(width: columnWidth * 0.7, height: max(2, hA))
                                }
                            }
                            .frame(width: columnWidth, height: height)
                        } else {
                            // Single bar
                            ZStack(alignment: .bottom) {
                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.barBackground)
                                    .frame(width: columnWidth, height: height)

                                if bucket.appSeconds > 0 {
                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.accent)
                                        .frame(width: columnWidth, height: max(2, barHeight))
                                }
                            }
                            .frame(width: columnWidth, height: height)
                        }
                    }
                }
            }
            .frame(height: height)

            // Hour axis labels (00, 06, 12, 18, 23)
            HStack {
                Text("00")
                Spacer()
                Text("06")
                Spacer()
                Text("12")
                Spacer()
                Text("18")
                Spacer()
                Text("23")
            }
            .font(.system(size: 9, weight: .regular, design: .monospaced))
            .foregroundColor(ThemeColors.textDim)
        }
    }
}
