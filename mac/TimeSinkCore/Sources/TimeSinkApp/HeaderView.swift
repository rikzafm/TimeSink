import SwiftUI
import TimeSinkCore

public struct HeaderView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    private var isAll: Bool { state.selectedTab == .all }
    private var isToday: Bool { state.selectedTab == .today }
    private var isWrapped: Bool { state.selectedTab == .year && state.yearSubView == .wrapped }

    public var body: some View {
        let wide = isToday || isWrapped
        HStack(spacing: isAll ? 6 : 2) {
            HStack(spacing: 9) {
                Circle()
                    .fill(ThemeColors.accent)
                    .frame(width: 9, height: 9)

                Text("TimeSink")
                    .font(.system(size: 17, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            Segmented(
                options: [(DeviceScope.this, "This Mac"), (DeviceScope.all, "All devices")],
                selection: Binding(get: { state.deviceScope }, set: { state.setDeviceScope($0) })
            )
            .padding(.trailing, 6)

            Button(action: { state.toggleGamesOnly() }) {
                IconView(.gamepad, size: 20, weight: 1.8, color: state.gamesOnly ? ThemeColors.accent : ThemeColors.textDim)
                    .frame(width: isAll ? 30 : 28, height: isAll ? 30 : 28)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Games only")

            Button(action: {
                state.showingDevicesSync = false
                state.showingSettings = true
            }) {
                IconView(.gear, size: 20, weight: 1.8, color: ThemeColors.textDim)
                    .frame(width: isAll ? 30 : 28, height: isAll ? 30 : 28)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Settings")
        }
        .padding(.leading, wide || isAll ? 16 : 14)
        .padding(.trailing, wide ? 12 : 14)
        .frame(height: isAll ? 48 : 44)
    }
}
