import Foundation

public struct DayDetail: Sendable, Equatable {
    public let date: String
    public let totalSeconds: Int64
    public let firstStartUtc: Int64?
    public let lastEndUtc: Int64?
    public let apps: [BarApp]
    public let weekAverageSeconds: Double
    public let sameDayLastWeekSeconds: Int64

    public init(
        date: String,
        totalSeconds: Int64,
        firstStartUtc: Int64?,
        lastEndUtc: Int64?,
        apps: [BarApp],
        weekAverageSeconds: Double,
        sameDayLastWeekSeconds: Int64
    ) {
        self.date = date
        self.totalSeconds = totalSeconds
        self.firstStartUtc = firstStartUtc
        self.lastEndUtc = lastEndUtc
        self.apps = apps
        self.weekAverageSeconds = weekAverageSeconds
        self.sameDayLastWeekSeconds = sameDayLastWeekSeconds
    }
}

public enum DayDetailQuery {
    public static func run(_ context: UsageContext, day: String) throws -> DayDetail {
        try UsageWindow.over(context: context) { window in
            run(window: window, day: day)
        }
    }

    public static func run(window: UsageWindow, day: String) -> DayDetail {
        let thisDay = TodayQuery.run(window: window, forDay: day)

        let df = DateFormatter()
        df.dateFormat = "yyyy-MM-dd"
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = window.Context.zone

        var lastWeekDay = day
        if let d = df.date(from: day), let prevD = calendar.date(byAdding: .day, value: -7, to: d) {
            lastWeekDay = df.string(from: prevD)
        }
        let lastWeek = TodayQuery.run(window: window, forDay: lastWeekDay, useSignIn: false)

        let dow = window.Context.weekStart
        var weeksBack = 0
        let targetWeekStart = WeekStarts.containing(day: day, weekStart: dow)
        let currentWeekStart = WeekStarts.containing(day: window.todayStr, weekStart: dow)
        if let eDate = df.date(from: targetWeekStart), let cDate = df.date(from: currentWeekStart) {
            let diffDays = calendar.dateComponents([.day], from: eDate, to: cDate).day ?? 0
            weeksBack = max(0, diffDays / 7)
        }
        let week = WeekQuery.run(window: window, weeksBack: weeksBack)

        let apps = thisDay.rows.map { BarApp(appId: $0.appId, name: $0.name, seconds: $0.seconds) }
        let allSessions = thisDay.rows.flatMap { $0.sessions }
        let first = allSessions.min(by: { $0.startUtc < $1.startUtc })?.startUtc
        let last = allSessions.max(by: { $0.endUtc < $1.endUtc })?.endUtc
        let col = week.days.first(where: { $0.date == day })
        let total = col?.seconds ?? thisDay.uptimeSeconds
        let countedDays = max(1, week.days.filter { !$0.isFuture }.count)
        let weekAvg = Double(week.totalSeconds) / Double(countedDays)

        return DayDetail(
            date: day,
            totalSeconds: total,
            firstStartUtc: first,
            lastEndUtc: last,
            apps: apps,
            weekAverageSeconds: weekAvg,
            sameDayLastWeekSeconds: lastWeek.uptimeSeconds
        )
    }
}

