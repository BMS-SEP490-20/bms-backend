using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Services;

Console.WriteLine("=================================================");
Console.WriteLine("🧪 UC58 Automated Verification Suite: Send Push Notifications");
Console.WriteLine("=================================================");

int passed = 0;
int total = 0;

void AssertEqual<T>(string checkName, T actual, T expected)
{
    total++;
    if (EqualityComparer<T>.Default.Equals(actual, expected))
    {
        Console.WriteLine($"  [PASS] {checkName} (Expected: {expected}, Got: {actual})");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {checkName} (Expected: {expected}, Got: {actual})");
        Console.ResetColor();
    }
}

void AssertTrue(string checkName, bool condition)
{
    total++;
    if (condition)
    {
        Console.WriteLine($"  [PASS] {checkName}");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {checkName} (Condition was FALSE)");
        Console.ResetColor();
    }
}

var notifService = new PushNotificationService();

// Test 1: Register Device Token
Console.WriteLine("\n--- Check 1: Register Device Token ---");
var regResult = await notifService.RegisterDeviceTokenAsync(new RegisterDeviceTokenRequestDto
{
    UserId = "USER-TEST-001",
    DeviceToken = "fcm_test_token_abc123",
    Platform = "Android"
});
AssertTrue("Device token registered successfully", regResult);

// Test 2: Dispatch Normal Notification
Console.WriteLine("\n--- Check 2: Dispatch Normal Notification ---");
var req1 = new DispatchNotificationRequestDto
{
    RecipientUserId = "USER-TEST-001",
    Title = "Lịch hẹn lấy mẫu xét nghiệm",
    Message = "Lịch hẹn xét nghiệm của bạn đã được xác nhận vào lúc 08:00 sáng mai.",
    Channel = "PUSH_FCM",
    Priority = "NORMAL",
    ActionUrl = "/appointments/APT-001"
};
var res1 = await notifService.DispatchNotificationAsync(req1);
AssertTrue("Dispatch succeeds", res1.Success);
AssertEqual("Status is DELIVERED", res1.Status, "DELIVERED");
AssertTrue("NotificationId generated", !string.IsNullOrEmpty(res1.NotificationId));
AssertEqual("Channel is PUSH_FCM", res1.Channel, "PUSH_FCM");

// Test 3: Get User Notifications
Console.WriteLine("\n--- Check 3: Get User Notifications ---");
var userNotifs = await notifService.GetUserNotificationsAsync("USER-TEST-001");
AssertTrue("User notifications has at least 1 item", userNotifs.Items.Count >= 1);
AssertEqual("First item has matching title", userNotifs.Items[0].Title, "Lịch hẹn lấy mẫu xét nghiệm");

// Test 4: Mark Notification As READ
Console.WriteLine("\n--- Check 4: Mark Notification As READ ---");
var readSuccess = await notifService.MarkAsReadAsync(res1.NotificationId);
AssertTrue("Mark as read succeeds", readSuccess);

// Test 5: Mark All User Notifications As READ
Console.WriteLine("\n--- Check 5: Mark All User Notifications As READ ---");
var readCount = await notifService.MarkAllAsReadAsync("USER-TEST-001");
AssertTrue("Mark all as read completes", readCount >= 0);

// Test 6: Anti-Spam Rate Limiting (Max 5/minute for normal priority)
Console.WriteLine("\n--- Check 6: Anti-Spam Rate Limiting (5/minute) ---");
string spamUser = "SPAM-USER-001";
int successCount = 0;
int rejectedCount = 0;

for (int i = 0; i < 7; i++)
{
    var r = await notifService.DispatchNotificationAsync(new DispatchNotificationRequestDto
    {
        RecipientUserId = spamUser,
        Title = $"Thông báo spam #{i + 1}",
        Message = $"Nội dung thử nghiệm rate limit #{i + 1}",
        Priority = "NORMAL"
    });

    if (r.Success) successCount++;
    else rejectedCount++;
}

AssertEqual("Exactly 5 notifications allowed in 1-minute window", successCount, 5);
AssertEqual("2 notifications rejected due to rate limit", rejectedCount, 2);

// Test 7: Critical Panic Bypass of Rate Limit
Console.WriteLine("\n--- Check 7: Critical Panic Bypasses Rate Limit ---");
var panicReq = new DispatchNotificationRequestDto
{
    RecipientUserId = spamUser,
    Title = "CẢNH BÁO NGUY HIỂM TÍNH MẠNG (PANIC)",
    Message = "Chỉ số xét nghiệm Troponin T vượt ngưỡng cấp cứu khẩn cấp!",
    Priority = "CRITICAL_PANIC"
};
var panicRes = await notifService.DispatchNotificationAsync(panicReq);
AssertTrue("CRITICAL_PANIC bypasses rate limiting successfully", panicRes.Success);
AssertEqual("Status is DELIVERED for CRITICAL_PANIC", panicRes.Status, "DELIVERED");

// Test 8: Audit Logs Query with Filters
Console.WriteLine("\n--- Check 8: Audit Logs Query with Filters ---");
var auditLogs = await notifService.GetAuditLogsAsync(new PushNotificationQueryParameters
{
    Priority = "CRITICAL_PANIC"
});
AssertTrue("Audit logs contain at least 1 CRITICAL_PANIC notification", auditLogs.Items.Count >= 1);
AssertTrue("Filtered item is CRITICAL_PANIC", auditLogs.Items[0].Priority == "CRITICAL_PANIC");

Console.WriteLine("\n=================================================");
Console.WriteLine($"RESULTS: {passed}/{total} checks PASSED!");
Console.WriteLine("=================================================");

if (passed != total)
{
    Environment.Exit(1);
}
