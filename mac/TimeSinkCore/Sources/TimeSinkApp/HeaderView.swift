import SwiftUI
import TimeSinkCore

public struct HeaderView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        HStack(spacing: 12) {
            // Wordmark
            HStack(spacing: 6) {
                Text("TimeSink")
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)

                Circle()
                    .fill(ThemeColors.accent)
                    .frame(width: 6, height: 6)
            }

            Spacer()

            // Device Scope Control (segmented)
            if state.hasMultipleDevices {
                HStack(spacing: 0) {
                    Button(action: {
                        state.setDeviceScope(.this)
                    }) {
                        Text(state.localDeviceName)
                            .font(.system(size: 11, weight: state.deviceScope == .this ? .semibold : .regular))
                            .foregroundColor(state.deviceScope == .this ? ThemeColors.textPrimary : ThemeColors.textMuted)
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
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
                            .padding(.vertical, 3)
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
            }

            // Games toggle
            Button(action: {
                state.toggleGamesOnly()
            }) {
                Image(systemName: "gamecontroller.fill")
                    .font(.system(size: 13))
                    .foregroundColor(state.gamesOnly ? ThemeColors.accent : ThemeColors.textDim)
                    .frame(width: 24, height: 24)
                    .background(state.gamesOnly ? ThemeColors.surfaceRaised : Color.clear)
                    .cornerRadius(5)
            }
            .buttonStyle(.plain)
            .help("Filter games only")

            // Settings button
            Button(action: {
                state.showingSettings = true
            }) {
                Image(systemName: "gearshape")
                    .font(.system(size: 13))
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
