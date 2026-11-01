import SwiftUI
import TimeSinkCore

public struct HeaderView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        HStack(spacing: 10) {
            // Wordmark with leading green dot
            HStack(spacing: 7) {
                Circle()
                    .fill(ThemeColors.accent)
                    .frame(width: 8, height: 8)

                Text("TimeSink")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }

            Spacer()

            // Device Scope Control (segmented)
            HStack(spacing: 0) {
                Button(action: {
                    state.setDeviceScope(.this)
                }) {
                    Text("This Mac")
                        .font(.system(size: 11, weight: state.deviceScope == .this ? .semibold : .regular))
                        .foregroundColor(state.deviceScope == .this ? ThemeColors.textPrimary : ThemeColors.textMuted)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(state.deviceScope == .this ? ThemeColors.surfaceRaised : Color.clear)
                        .cornerRadius(4)
                }
                .buttonStyle(.plain)

                Button(action: {
                    state.setDeviceScope(.all)
                }) {
                    Text("All devices")
                        .font(.system(size: 11, weight: state.deviceScope == .all ? .semibold : .regular))
                        .foregroundColor(state.deviceScope == .all ? ThemeColors.textPrimary : ThemeColors.textMuted)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(state.deviceScope == .all ? ThemeColors.surfaceRaised : Color.clear)
                        .cornerRadius(4)
                }
                .buttonStyle(.plain)
            }
            .padding(2)
            .background(ThemeColors.surface)
            .cornerRadius(6)
            .overlay(
                RoundedRectangle(cornerRadius: 6)
                    .stroke(ThemeColors.border, lineWidth: 1)
            )

            // Devices & sync shortcut button
            Button(action: {
                state.showingSettings = true
                state.showingDevicesSync = true
            }) {
                ZStack {
                    RoundedRectangle(cornerRadius: 5)
                        .stroke(ThemeColors.border, lineWidth: 1)
                        .frame(width: 24, height: 20)

                    Image(systemName: "plus")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundColor(ThemeColors.textMuted)
                }
                .frame(width: 26, height: 24)
            }
            .buttonStyle(.plain)
            .help("Devices and sync")

            // Settings button
            Button(action: {
                state.showingDevicesSync = false
                state.showingSettings = true
            }) {
                Image(systemName: "gearshape")
                    .font(.system(size: 14))
                    .foregroundColor(ThemeColors.textMuted)
                    .frame(width: 24, height: 24)
            }
            .buttonStyle(.plain)
            .help("Settings")
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(ThemeColors.background)
        .overlay(
            Rectangle()
                .frame(height: 1)
                .foregroundColor(ThemeColors.borderSubtle),
            alignment: .bottom
        )
    }
}

