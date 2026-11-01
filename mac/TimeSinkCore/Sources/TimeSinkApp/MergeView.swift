import SwiftUI
import TimeSinkCore

public struct MergeView: View {
    @ObservedObject var state: AppState
    public let mainAppId: Int64
    public let onDismiss: () -> Void

    @State private var plan: MergePlan?
    @State private var selectedDevice: String = "pc-1"
    @State private var searchQuery: String = ""
    @State private var filterTab: String = "Tracked"

    public init(state: AppState, mainAppId: Int64, onDismiss: @escaping () -> Void) {
        self.state = state
        self.mainAppId = mainAppId
        self.onDismiss = onDismiss
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Button(action: onDismiss) {
                    HStack(spacing: 4) {
                        Image(systemName: "chevron.left")
                            .font(.system(size: 11, weight: .bold))
                        Text("Merge into “\(plan?.mainName ?? "Chrome")”")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    .foregroundColor(ThemeColors.textPrimary)
                }
                .buttonStyle(.plain)

                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            .background(ThemeColors.surface)

            // Device Tabs
            HStack(spacing: 16) {
                HStack(spacing: 6) {
                    Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                    Text("This Mac")
                        .font(.system(size: 12, weight: selectedDevice == "mac" ? .semibold : .medium))
                        .foregroundColor(selectedDevice == "mac" ? ThemeColors.textPrimary : ThemeColors.textMuted)
                }
                .onTapGesture { selectedDevice = "mac" }

                HStack(spacing: 6) {
                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                    Text("Desktop PC")
                        .font(.system(size: 12, weight: selectedDevice == "pc-1" ? .semibold : .medium))
                        .foregroundColor(selectedDevice == "pc-1" ? ThemeColors.textPrimary : ThemeColors.textMuted)

                    Text("1")
                        .font(.system(size: 9, weight: .bold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 5)
                        .padding(.vertical, 1)
                        .background(ThemeColors.accent)
                        .cornerRadius(8)
                }
                .onTapGesture { selectedDevice = "pc-1" }

                HStack(spacing: 6) {
                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                    Text("Work laptop")
                        .font(.system(size: 12, weight: selectedDevice == "pc-2" ? .semibold : .medium))
                        .foregroundColor(selectedDevice == "pc-2" ? ThemeColors.textPrimary : ThemeColors.textMuted)
                }
                .onTapGesture { selectedDevice = "pc-2" }

                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .background(ThemeColors.background)

            // Description
            Text("Tick the apps to count under this one. Their time is added together in every view. Untick a merged app to unmerge it.")
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
                .lineSpacing(2)
                .padding(.horizontal, 16)
                .padding(.vertical, 4)

            // Search and filter row
            HStack(spacing: 8) {
                HStack(spacing: 6) {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)

                    TextField("Search apps on Desktop PC", text: $searchQuery)
                        .textFieldStyle(.plain)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textPrimary)
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.border, lineWidth: 1)
                )

                // Filter pills
                HStack(spacing: 0) {
                    ForEach(["Tracked", "Untracked", "All"], id: \.self) { t in
                        Button(action: { filterTab = t }) {
                            Text(t)
                                .font(.system(size: 10, weight: filterTab == t ? .semibold : .regular))
                                .foregroundColor(filterTab == t ? ThemeColors.textPrimary : ThemeColors.textMuted)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 4)
                                .background(filterTab == t ? ThemeColors.surfaceRaised : Color.clear)
                                .cornerRadius(4)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(2)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.border, lineWidth: 1)
                )
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 6)

            // Candidate apps list
            ScrollView {
                VStack(spacing: 4) {
                    let candidates: [(key: String, initial: String, name: String, sub: String, hours: String, checked: Bool)] = [
                        ("chrome", "C", "chrome.exe", "Google Chrome · tracked", "159 h", true),
                        ("code", "V", "Code.exe", "Visual Studio Code · tracked", "112 h", false),
                        ("spotify", "S", "Spotify.exe", "Spotify · tracked", "48 h", false),
                        ("discord", "D", "Discord.exe", "Discord · tracked", "51 h", false),
                        ("steam", "S", "steam.exe", "Valve · tracked · Background", "210 h", false)
                    ]

                    ForEach(candidates, id: \.key) { c in
                        HStack(spacing: 10) {
                            ZStack {
                                RoundedRectangle(cornerRadius: 4)
                                    .stroke(c.checked ? ThemeColors.accent : ThemeColors.border, lineWidth: 1)
                                    .background(c.checked ? ThemeColors.accent : Color.clear)
                                    .frame(width: 16, height: 16)

                                if c.checked {
                                    Image(systemName: "checkmark")
                                        .font(.system(size: 10, weight: .bold))
                                        .foregroundColor(ThemeColors.background)
                                }
                            }
                            .frame(width: 20, height: 20)

                            ZStack {
                                RoundedRectangle(cornerRadius: 4)
                                    .fill(ThemeColors.surfaceRaised)
                                    .frame(width: 24, height: 24)
                                Text(c.initial)
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            VStack(alignment: .leading, spacing: 2) {
                                Text(c.name)
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text(c.sub)
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            Text(c.hours)
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 7)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 4)

                // Calculation preview card
                HStack {
                    Text("Chrome (124 h) + chrome.exe (159 h) = 283 h under “Chrome”.")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)
                    Spacer()
                }
                .padding(10)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.accent.opacity(0.8), lineWidth: 1)
                )
                .padding(.horizontal, 16)
                .padding(.top, 6)

                // Explanation footnote
                Text("Ticked untracked apps start being tracked now and count under Chrome. Ticks on other devices stay: This Mac 0 · Work laptop 0.")
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textDim)
                    .lineSpacing(2)
                    .padding(.horizontal, 16)
                    .padding(.top, 4)
            }

            // Bottom action buttons (Cancel & Merge 1 into Chrome)
            HStack(spacing: 10) {
                Button(action: onDismiss) {
                    Text("Cancel")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 8)
                        .background(ThemeColors.surfaceRaised)
                        .cornerRadius(6)
                }
                .buttonStyle(.plain)

                Button(action: {
                    if let p = plan {
                        applyMerge(plan: p)
                    } else {
                        onDismiss()
                    }
                }) {
                    Text("Merge 1 into Chrome")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(ThemeColors.background)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 8)
                        .background(ThemeColors.accent)
                        .cornerRadius(6)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.borderSubtle),
                alignment: .top
            )
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .onAppear {
            loadPlan()
        }
    }

    private func loadPlan() {
        let lib = MacAppLibrary(db: state.db)
        if let p = try? lib.openMergePlan(mainId: mainAppId) {
            self.plan = p
        }
    }

    private func applyMerge(plan: MergePlan) {
        let lib = MacAppLibrary(db: state.db)
        let tickedAppIds = plan.candidates.filter { plan.isTicked($0.key) }.map(\.appId)
        try? lib.merge(mainId: mainAppId, candidateIds: tickedAppIds)
        state.refresh()
        onDismiss()
    }
}
