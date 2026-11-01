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

    private var currentHour: Int {
        Calendar.current.component(.hour, from: Date())
    }

    private var currentTimeString: String {
        let df = DateFormatter()
        df.dateFormat = "h:mm a"
        return df.string(from: Date()).lowercased()
    }

    public var body: some View {
        VStack(spacing: 8) {
            // Current time pointer indicator
            HStack {
                let frac = CGFloat(max(0, min(23, currentHour))) / 23.0
                Spacer()
                    .frame(width: max(0, frac * 280))

                VStack(spacing: 1) {
                    Text(currentTimeString)
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(ThemeColors.accent)
                        .cornerRadius(4)

                    Image(systemName: "arrowtriangle.down.fill")
                        .font(.system(size: 6))
                        .foregroundColor(ThemeColors.accent)
                        .offset(y: -1)
                }

                Spacer()
            }
            .frame(height: 18)

            // Bars Grid
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
                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.barBackground)
                                    .frame(width: columnWidth, height: height)

                                if hA > 0 && hB > 0 {
                                    // Both on: show overlap cyan/teal bar or stacked
                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceA)
                                        .frame(width: columnWidth * 0.7, height: max(2, hA))

                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceB)
                                        .frame(width: columnWidth * 0.7, height: max(2, hB))
                                        .offset(x: columnWidth * 0.3)
                                } else if hA > 0 {
                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceA)
                                        .frame(width: columnWidth, height: max(2, hA))
                                } else if hB > 0 {
                                    RoundedRectangle(cornerRadius: 1.5)
                                        .fill(ThemeColors.deviceB)
                                        .frame(width: columnWidth, height: max(2, hB))
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

            // Hour axis labels (12 am, 6 am, 12 pm, 6 pm, 12 am)
            HStack {
                Text("12 am")
                Spacer()
                Text("6 am")
                Spacer()
                Text("12 pm")
                Spacer()
                Text("6 pm")
                Spacer()
                Text("12 am")
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textDim)

            // Legend below chart
            HStack(spacing: 12) {
                if isAllScope {
                    HStack(spacing: 4) {
                        RoundedRectangle(cornerRadius: 1.5)
                            .fill(ThemeColors.deviceA)
                            .frame(width: 8, height: 8)
                        Text("Mac on")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    HStack(spacing: 4) {
                        RoundedRectangle(cornerRadius: 1.5)
                            .fill(ThemeColors.deviceB)
                            .frame(width: 8, height: 8)
                        Text("PC on")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    HStack(spacing: 4) {
                        RoundedRectangle(cornerRadius: 1.5)
                            .fill(Color(red: 0.35, green: 0.85, blue: 0.75))
                            .frame(width: 8, height: 8)
                        Text("Overlapping = both on")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    HStack(spacing: 3) {
                        Text("••••")
                            .font(.system(size: 8))
                            .foregroundColor(ThemeColors.textDim)
                        Text("Typical day")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    HStack(spacing: 3) {
                        Text("///")
                            .font(.system(size: 8))
                            .foregroundColor(ThemeColors.textDim)
                        Text("Asleep")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                } else {
                    HStack(spacing: 4) {
                        RoundedRectangle(cornerRadius: 1.5)
                            .fill(ThemeColors.accent)
                            .frame(width: 8, height: 8)
                        Text("Apps open")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    HStack(spacing: 3) {
                        Text("••••")
                            .font(.system(size: 8))
                            .foregroundColor(ThemeColors.textDim)
                        Text("Typical day")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    HStack(spacing: 3) {
                        Text("///")
                            .font(.system(size: 8))
                            .foregroundColor(ThemeColors.textDim)
                        Text("Mac asleep · paused")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }
            }
            .padding(.top, 4)
        }
    }
}

