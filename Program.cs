using System.Threading;
using RedditSharp;
using RedditSharp.Things;
using System;
using System.Linq;

namespace rJacksonvilleModBot
{
    class Program
    {
        private const string DailyCommentFormat = "Reply here with events on {0}.";
        private const string MonthlyPostTitleFormat = "Post {0} {1} Events Here";//"Jacksonville Events Calendar: Post {0} {1} Events Here";
        private const string SidebarSectionMarkdown = "#**Events and Entertainment**";
        private const string SidebarSectionAdditional = "\r\n**More event & entertainment resources**\r\n\r\n* [Downtown Jacksonville](http://downtownjacksonville.org/Downtown_Vision_Inc_Home.aspx)\r\n* [Jax.com Events](http://events.jacksonville.com/)\r\n* [JaxEvents](http://www.jaxevents.com/)\r\n* [Folio Events](http://folioweekly.com/calendar)\r\n* [Jax4Kids - Family Friendly Events](http://jax4kids.com/)";
        private const string SubredditName = "/r/Jacksonville";

        private static Post GetOrCreateMonthlyPost(Reddit reddit, Subreddit subreddit, AuthenticatedUser user, int month, int year)
        {
            var firstDateOfMonth = new DateTime(year, month, 1);
            var monthName = firstDateOfMonth.ToString("MMMM");
            var monthPostTitle = string.Format(MonthlyPostTitleFormat, monthName, year);
            var monthPosts = user.Posts.Where(p => p.Author.Name == user.Name && p.Title == monthPostTitle).OrderByDescending(p => p.Created);

            if (monthPosts.Any())
            {
                if (monthPosts.Count() > 1)
                    reddit.ComposePrivateMessage("Multiple monthly posts found", "Multiple posts found for " + monthName + " monthly post." + Environment.NewLine + string.Join(Environment.NewLine, monthPosts.Select(p => p.Shortlink)), SubredditName);

                return monthPosts.First();
            }

            // Create the post for the month if it doesn't exist.
            var post = subreddit.SubmitTextPost(monthPostTitle, "Know of something going on in " + monthName + "? Post it here under the appropriately dated comment. When the date comes around, it'll be linked to from the sidebar.");
            var dailyCommentDate = firstDateOfMonth;

            while (dailyCommentDate.Month == month)
            {
                post.Comment(string.Format(DailyCommentFormat, dailyCommentDate.ToString("MMMM d, yyyy")));

                dailyCommentDate = dailyCommentDate.AddDays(1);
                
                Thread.Sleep(10000); // Wait 10 seconds before posting another comment
            }

            reddit.ComposePrivateMessage("New monthly post created", "Monthly events post created for " + monthName + " at " + post.Shortlink + ".", SubredditName);

            return post;
        }

        static void Main(string[] args)
        {
            var reddit = new Reddit();
            var user = reddit.LogIn(args[0], args[1]);
            var subreddit = reddit.GetSubreddit(SubredditName);

            AppDomain.CurrentDomain.UnhandledException += (o, e) => reddit.ComposePrivateMessage("Exception", e.ExceptionObject.ToString(), SubredditName);

            subreddit.Subscribe();

            if (!user.ModeratorSubreddits.Any(s => s.ToString().Equals(SubredditName, StringComparison.OrdinalIgnoreCase)))
            {
                reddit.ComposePrivateMessage("Bot user not a moderator", "The user '" + args[0] + "' is not a moderator for the " + SubredditName + " subreddit.", SubredditName);
                return;
            }

            // Reply to any private messages that have been sent to the mod bot.
            foreach (var message in user.PrivateMessages.Where(m => m.Unread))
            {
                message.Reply("You have messaged the " + SubredditName + " moderator bot. These private messages are not actively monitored.");
                message.SetAsRead();
            }

            // Get or create the post for this month
            var today = DateTime.Now;
            var thisMonthPost = GetOrCreateMonthlyPost(reddit, subreddit, user, today.Month, today.Year);
            var todaysComments = thisMonthPost.Comments.Where(c => c.Author == user.Name && c.Body == string.Format(DailyCommentFormat, today.ToString("MMMM d, yyyy"))).ToList();

            if (todaysComments.Any())
            {
                if (todaysComments.Count() > 1)
                    reddit.ComposePrivateMessage("Multiple daily posts found", "Multiple posts found for " + today.ToString("MMMM d, yyyy") + " daily post." + Environment.NewLine + string.Join(Environment.NewLine, todaysComments.Select(c => c.Shortlink)), SubredditName);

                var settings = subreddit.Settings;
                var sidebar = settings.Sidebar;

                if (!string.IsNullOrEmpty(sidebar) && sidebar.Contains(SidebarSectionMarkdown))
                {
                    var startIndex = sidebar.IndexOf(SidebarSectionMarkdown, StringComparison.Ordinal) + SidebarSectionMarkdown.Length;
                    var endIndex = sidebar.IndexOf("#**", startIndex, StringComparison.Ordinal); // Find the beginning of the next section 

                    if (endIndex < 0)
                        endIndex = sidebar.Length - 1; // There's no next section, so just replace the rest of the content

                    var newSidebarContent = "* [" + today.DayOfWeek + "](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [" + today.ToString("MMMM") + "](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [" + today.Day + "](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + Environment.NewLine + "[](http://example.com)" + Environment.NewLine;
                    newSidebarContent += Environment.NewLine + "* [There are " + todaysComments.First().Comments.Count() + " events today. Check it out or add your own.](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [Additionally, there are " + thisMonthPost.Comments.Where(c => c.Author == user.Name).Sum(c => c.Comments.Count) + " events posted for this month.](" + thisMonthPost.Shortlink + ")";
                    newSidebarContent += Environment.NewLine + SidebarSectionAdditional;

                    settings.Sidebar = sidebar.Remove(startIndex, endIndex - startIndex).Insert(startIndex, Environment.NewLine + Environment.NewLine + newSidebarContent + Environment.NewLine + Environment.NewLine);
                    settings.UpdateSettings();
                }
                else
                {
                    reddit.ComposePrivateMessage("No sidebar section found", "There was no sidebar section found for updating.", SubredditName);
                }
            }
            else
            {
                reddit.ComposePrivateMessage("No daily post found", "There was no daily comment found for " + today.ToString("MMMM d, yyyy") + ".", SubredditName);
            }

            // Ensure next months post exists if we're more than 3 weeks into the current month
            if (today.Day > 21)
            {
                var nextMonth = today.AddMonths(1);
                var nextMonthPost = GetOrCreateMonthlyPost(reddit, subreddit, user, nextMonth.Month, nextMonth.Year);
            }

            // TODO: Find or add the API methods for automatically making certain Monthly posts sticky.
        }
    }
}
