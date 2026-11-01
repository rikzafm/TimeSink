import SwiftUI

public struct TabBarView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        HStack(spacing: 0) {
            ForEach(AppTab.allCases) { tab in
                Button(action: {
                    state.selectedTab = tab
                    state.refresh()
                }) {
                    VStack(spacing: 6) {
                        Text(tab.rawValue)
                            .font(.system(size: 13, weight: state.selectedTab == tab ? .semibold : .medium))
                            .foregroundColor(state.selectedTab == tab ? ThemeColors.textPrimary : ThemeColors.textMuted)

                        Rectangle()
                            .fill(state.selectedTab == tab ? ThemeColors.accent : Color.clear)
                            .frame(height: 2)
                    }
                    .frame(maxWidth: .infinity)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 12)
        .padding(.top, 6)
        .background(ThemeColors.background)
        .overlay(
            Rectangle()
                .frame(height: 1)
                .foregroundColor(ThemeColors.borderSubtle),
            alignment: .bottom
        )
    }
}
