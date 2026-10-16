import Foundation
// Sends the same message MN Finder sends to a running preview.
let path = CommandLine.arguments[1]
DistributedNotificationCenter.default().postNotificationName(Notification.Name("com.mavinokta.player.preview.started"), object: nil, userInfo: nil, deliverImmediately: true)
DistributedNotificationCenter.default().postNotificationName(Notification.Name("com.mavinokta.player.preview.open"), object: nil, userInfo: ["path": path], deliverImmediately: true)
RunLoop.main.run(until: Date().addingTimeInterval(0.5))
