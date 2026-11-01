import SwiftUI

public struct TabBarView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        HStack(spacing: 0) {
            ForEach(AppTab.allCases) { tab in
                let selected = state.selectedTab == tab
                Button(action: {
                    state.selectedTab = tab
                    state.refresh()
                }) {
                    Text(tab.rawValue)
                        .font(.system(size: 15, weight: selected ? .semibold : .regular))
                        .foregroundColor(selected ? ThemeColors.accent : ThemeColors.textMuted)
                        .frame(maxWidth: .infinity)
                        .frame(height: 40)
                        .overlay(Rectangle().fill(selected ? ThemeColors.accent : .clear).frame(height: 2), alignment: .bottom)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .bottomRule()
    }
}
