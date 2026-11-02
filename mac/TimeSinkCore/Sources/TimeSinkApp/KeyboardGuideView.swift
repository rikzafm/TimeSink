import SwiftUI
import TimeSinkCore

public struct KeyboardGuideView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        VStack(spacing: 0) {
            header
            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 14) {
                    sectionHeader("NAVIGATION & TABS")
                    shortcutCard(
                        action: "Switch to tab",
                        desc: "Today (1), Week (2), Month (3), Year (4), All (5)",
                        badges: [["⌘", "1..5"]]
                    )
                    shortcutCard(
                        action: "Next tab",
                        desc: "Cycle forward across tabs",
                        badges: [["⌃", "Tab"], ["⌘", "⇧", "]"]]
                    )
                    shortcutCard(
                        action: "Previous tab",
                        desc: "Cycle backward across tabs",
                        badges: [["⌃", "⇧", "Tab"], ["⌘", "⇧", "["]]
                    )
                    shortcutCard(
                        action: "Date back / forward",
                        desc: "Step days, weeks, months, or years",
                        badges: [["⌥", "← / →"], ["⌘", "[ / ]"]]
                    )

                    sectionHeader("GO BACK & EXIT")
                    shortcutCard(
                        action: "Escape (Hierarchical exit)",
                        desc: "1. Clears search/input text\n2. Closes dialogs, sheets & sub-screens\n3. Closes overlays (Settings, Add App, Detail)\n4. Resets historical day to live Today\n5. Dismisses popover (tracking stays active)",
                        badges: [["Esc"]]
                    )
                    shortcutCard(
                        action: "Secondary go back",
                        desc: "Pops back one view in overlays and drill-downs",
                        badges: [["⌘", "["], ["Delete"]]
                    )

                    sectionHeader("QUICK ACTIONS")
                    shortcutCard(
                        action: "Add app",
                        desc: "Open the Add App overlay",
                        badges: [["⌘", "N"]]
                    )
                    shortcutCard(
                        action: "Games filter",
                        desc: "Toggle Games-only view",
                        badges: [["⌘", "G"]]
                    )
                    shortcutCard(
                        action: "Settings",
                        desc: "Toggle the Settings screen",
                        badges: [["⌘", ","]]
                    )
                    shortcutCard(
                        action: "Shortcuts guide",
                        desc: "Open this guide from anywhere",
                        badges: [["?"]]
                    )
                }
                .padding(.horizontal, 14)
                .padding(.top, 8)
                .padding(.bottom, 20)
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
    }

    private var header: some View {
        HStack {
            Button(action: {
                state.showingKeyboardGuide = false
            }) {
                HStack(spacing: 4) {
                    Text("‹").font(.system(size: 16))
                    Text("Back").font(.system(size: 13))
                }
                .foregroundColor(ThemeColors.accent)
                .padding(.horizontal, 8)
                .frame(height: 28)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            Text("Keyboard shortcuts")
                .font(.system(size: 14, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(maxWidth: .infinity)
                .padding(.trailing, 56)
        }
        .padding(.horizontal, 10)
        .frame(height: 44)
        .bottomRule()
    }

    private func sectionHeader(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 10, weight: .bold))
            .foregroundColor(ThemeColors.textMuted)
            .padding(.top, 6)
            .padding(.bottom, 2)
    }

    private func shortcutCard(action: String, desc: String?, badges: [[String]]) -> some View {
        HStack(alignment: .center, spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                Text(action)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                if let desc = desc {
                    Text(desc)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                        .lineSpacing(2)
                }
            }

            Spacer()

            HStack(spacing: 6) {
                ForEach(0..<badges.count, id: \.self) { groupIdx in
                    if groupIdx > 0 {
                        Text("or")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                    }
                    HStack(spacing: 2) {
                        ForEach(badges[groupIdx], id: \.self) { key in
                            keyBadge(key)
                        }
                    }
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(
            RoundedRectangle(cornerRadius: 6)
                .fill(ThemeColors.surface)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(ThemeColors.borderSubtle, lineWidth: 1)
        )
    }

    private func keyBadge(_ key: String) -> some View {
        Text(key)
            .font(.system(size: 11, weight: .semibold, design: .monospaced))
            .foregroundColor(ThemeColors.textPrimary)
            .padding(.horizontal, 5)
            .padding(.vertical, 2)
            .background(
                RoundedRectangle(cornerRadius: 4)
                    .fill(ThemeColors.surfaceRaised)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 4)
                    .stroke(ThemeColors.border, lineWidth: 1)
            )
    }
}

