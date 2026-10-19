using System;
using Android.App;
using Android.Content;

namespace JianghuGo.Android
{
    /// <summary>
    /// Local reminders (1.9): retreat end, cauldron/forge batch, crops, expeditions, hunts. Scheduled on the device with an
    /// inexact AlarmManager alarm (no exact-alarm permission); nothing leaves the phone. Off until the player turns them on.
    /// </summary>
    public static class Alarms
    {
        public const string Channel = "jianghu_reminders";
        public const string PrefKey = "reminders_on";
        public const int PermissionRequestCode = 4711;

        public static bool Enabled(ISharedPreferences prefs) => prefs.GetBoolean(PrefKey, false);

        public static bool Allowed(Context context)
        {
            var nm = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            return nm.AreNotificationsEnabled();
        }

        public static void EnsureChannel(Context context)
        {
            var nm = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            if (nm.GetNotificationChannel(Channel) != null) return;
            nm.CreateNotificationChannel(new NotificationChannel(Channel, "Przypomnienia", NotificationImportance.Default)
            {
                Description = "Koniec odosobnienia, gotowe partie, plony i wyprawy",
            });
        }

        /// <summary>Schedules (or replaces) the reminder under this key; a past or missing time cancels it.</summary>
        public static void Schedule(Context context, ISharedPreferences prefs, string key, DateTimeOffset? when, string title, string text)
        {
            var pending = Pending(context, key, title, text);
            var am = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
            if (!Enabled(prefs) || when == null || when <= DateTimeOffset.UtcNow)
            {
                am.Cancel(pending);
                return;
            }

            EnsureChannel(context);
            am.SetAndAllowWhileIdle(AlarmType.RtcWakeup, when.Value.ToUnixTimeMilliseconds(), pending);
        }

        private static PendingIntent Pending(Context context, string key, string title, string text)
        {
            var intent = new Intent(context, typeof(ReminderReceiver));
            intent.PutExtra("title", title);
            intent.PutExtra("text", text);
            intent.PutExtra("id", key.GetHashCode());
            return PendingIntent.GetBroadcast(context, key.GetHashCode(), intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        }
    }

    [BroadcastReceiver(Enabled = true, Exported = false)]
    public sealed class ReminderReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (context == null || intent == null || !Alarms.Allowed(context)) return;
            Alarms.EnsureChannel(context);
            var open = PendingIntent.GetActivity(context, 0, new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
                PendingIntentFlags.Immutable);
            var builder = new Notification.Builder(context, Alarms.Channel);
            builder.SetSmallIcon(Resource.Drawable.app_icon)
                .SetContentTitle(intent.GetStringExtra("title"))
                .SetContentText(intent.GetStringExtra("text"))
                .SetAutoCancel(true)
                .SetContentIntent(open);
            var nm = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            nm.Notify(intent.GetIntExtra("id", 1), builder.Build());
        }
    }
}
