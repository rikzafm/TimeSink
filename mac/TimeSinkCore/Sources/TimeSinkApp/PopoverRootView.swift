import SwiftUI
import TimeSinkCore

public struct PopoverRootView: View {
    @State private var todayTotal: String = "0m"

    public init() {}

    public var body: some View {
        VStack(spacing: 0) {
            // Header bar
            HStack {
                Text("TimeSink")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(Color(red: 0.93, green: 0.95, blue: 0.90))

                Spacer()

                HStack(spacing: 6) {
                    Circle()
                        .stroke(Color(red: 0.78, green: 0.95, blue: 0.42), lineWidth: 1.5)
                        .frame(width: 10, height: 10)
                    Text(todayTotal)
                        .font(.system(size: 12, weight: .medium, design: .monospaced))
                        .foregroundColor(Color(red: 0.93, green: 0.95, blue: 0.90))
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 3)
                .background(Color(red: 0.17, green: 0.19, blue: 0.15))
                .cornerRadius(6)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            .background(Color(red: 0.09, green: 0.10, blue: 0.07))
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(Color(red: 0.14, green: 0.16, blue: 0.11)),
                alignment: .bottom
            )

            // Main body area (400 x 590)
            VStack {
                Spacer()
                Text("TimeSink for Mac")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(Color(red: 0.93, green: 0.95, blue: 0.90))
                Text("Tracking active applications")
                    .font(.system(size: 12))
                    .foregroundColor(Color(red: 0.60, green: 0.64, blue: 0.57))
                    .padding(.top, 4)
                Spacer()
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Color(red: 0.07, green: 0.08, blue: 0.06))
        }
        .frame(width: 400, height: 640)
    }
}
