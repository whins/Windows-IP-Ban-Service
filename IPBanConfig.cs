﻿#region Imports

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

#endregion Imports

namespace IPBan
{
    /// <summary>
    /// Configuration for ip ban app
    /// </summary>
    public class IPBanConfig
    {
        private ExpressionsToBlock expressions;
        private int failedLoginAttemptsBeforeBan = 5;
        private TimeSpan banTime = TimeSpan.FromDays(1.0d);
        private string banFile = "banlog.txt";
        private TimeSpan expireTime = TimeSpan.FromDays(1.0d);
        private TimeSpan cycleTime = TimeSpan.FromMinutes(1.0d);
        private string ruleName = "BlockIPAddresses";
        private readonly HashSet<string> whiteList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Regex whiteListRegex;
        private readonly HashSet<string> blackList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Regex blackListRegex;
        private readonly HashSet<string> allowedUserNames = new HashSet<string>();
        private bool banFileClearOnRestart;
		private string freeswitchLogFilePath;
		private int readFreeswitchLogTimeout;

        /// <summary>
        /// Checks whether a user name should be banned after a failed login attempt. Cases where this would happen would be if the config has specified an allowed list of user names.
        /// </summary>
        /// <param name="userName">User name to check for banning</param>
        /// <returns>True if the user name should be banned, false otherwise</returns>
        private bool ShouldBanUserNameAfterFailedLoginAttempt(string userName)
        {
            return (allowedUserNames.Count != 0 && !allowedUserNames.Contains(userName));
        }

        private void PopulateList(HashSet<string> set, ref Regex regex, string setValue, string regexValue)
        {
            setValue = (setValue ?? string.Empty).Trim();
            regexValue = (regexValue ?? string.Empty).Replace("*", @"[0-9A-Fa-f]+?").Trim();
            set.Clear();
            regex = null;

            if (!string.IsNullOrWhiteSpace(setValue))
            {
                IPAddress tmp;

                foreach (string v in setValue.Split(','))
                {
                    set.Add(v.Trim());

                    if (v != "0.0.0.0" && v != "::0" && IPAddress.TryParse(v, out tmp))
                    {
                        try
                        {
                            IPAddress[] addresses = Dns.GetHostEntry(v).AddressList;
                            if (addresses != null)
                            {
                                foreach (IPAddress adr in addresses)
                                {
                                    set.Add(adr.ToString());
                                }
                            }
                        }
                        catch (System.Net.Sockets.SocketException)
                        {
                            // ignore, dns lookup fails
                        }
                    }
                }
            }

            if (regexValue.Length != 0)
            {
                regex = new Regex(regexValue, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public IPBanConfig()
        {
            ConfigurationManager.RefreshSection("appSettings");
            ConfigurationManager.RefreshSection("configSections");
            ConfigurationManager.RefreshSection("nlog");
            ConfigurationManager.RefreshSection("ExpressionsToBlock");

            string value = ConfigurationManager.AppSettings["FailedLoginAttemptsBeforeBan"];
            failedLoginAttemptsBeforeBan = int.Parse(value);

            value = ConfigurationManager.AppSettings["BanTime"];
            banTime = TimeSpan.Parse(value);

            value = ConfigurationManager.AppSettings["BanFile"];
            banFile = value;
            if (!Path.IsPathRooted(banFile))
            {
                banFile = Path.GetFullPath(banFile);
            }
            value = ConfigurationManager.AppSettings["BanFileClearOnRestart"];
            if (!bool.TryParse(value, out banFileClearOnRestart))
            {
                banFileClearOnRestart = true;
            }

            value = ConfigurationManager.AppSettings["ExpireTime"];
            expireTime = TimeSpan.Parse(value);
            
            value = ConfigurationManager.AppSettings["CycleTime"];
            cycleTime = TimeSpan.Parse(value);

            value = ConfigurationManager.AppSettings["RuleName"];
            ruleName = value;

			value = ConfigurationManager.AppSettings["FreeswitchLogFilePath"];
			freeswitchLogFilePath = value;

			value = ConfigurationManager.AppSettings["ReadFreeswitchLogTimeout"];
			readFreeswitchLogTimeout = int.Parse(value);

            PopulateList(whiteList, ref whiteListRegex, ConfigurationManager.AppSettings["Whitelist"], ConfigurationManager.AppSettings["WhitelistRegex"]);
            PopulateList(blackList, ref blackListRegex, ConfigurationManager.AppSettings["Blacklist"], ConfigurationManager.AppSettings["BlacklistRegex"]);
            Regex ignored = null;
            PopulateList(allowedUserNames, ref ignored, ConfigurationManager.AppSettings["AllowedUserNames"], null);
            expressions = (ExpressionsToBlock)System.Configuration.ConfigurationManager.GetSection("ExpressionsToBlock");

            foreach (ExpressionsToBlockGroup group in expressions.Groups)
            {
                foreach (ExpressionToBlock expression in group.Expressions)
                {
                    expression.Regex = (expression.Regex ?? string.Empty).Trim();
                    expression.RegexObject = new Regex(expression.Regex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                }
            }
        }

        /// <summary>
        /// Check if an ip address is whitelisted
        /// </summary>
        /// <param name="ipAddress">IP Address</param>
        /// <returns>True if whitelisted, false otherwise</returns>
        public bool IsWhiteListed(string ipAddress)
        {
            IPAddress ip;

            return (whiteList.Contains(ipAddress) || !IPAddress.TryParse(ipAddress, out ip) || (whiteListRegex != null && whiteListRegex.IsMatch(ipAddress)));
        }

        /// <summary>
        /// Check if an ip address, dns name or user name is blacklisted
        /// </summary>
        /// <param name="text">Text containing ip address, dns name or user name</param>
        /// <returns>True if blacklisted, false otherwise</returns>
        public bool IsBlackListed(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && ((blackList.Contains(text) || (blackListRegex != null && blackListRegex.IsMatch(text))) || ShouldBanUserNameAfterFailedLoginAttempt(text));
        }

        /// <summary>
        /// Return all the groups that match the specified keywords
        /// </summary>
        /// <param name="keywords">Keywords</param>
        /// <returns>Groups that match</returns>
        public IEnumerable<ExpressionsToBlockGroup> GetGroupsMatchingKeywords(ulong keywords)
        {
            return Expressions.Groups.Where(g => (g.KeywordsULONG == keywords));
        }

        /// <summary>
        /// Number of failed login attempts before a ban is initiated
        /// </summary>
        public int FailedLoginAttemptsBeforeBan { get { return failedLoginAttemptsBeforeBan; } }

        /// <summary>
        /// Length of time to ban an ip address
        /// </summary>
        public TimeSpan BanTime { get { return banTime; } }

        /// <summary>
        /// Ban file
        /// </summary>
        public string BanFile { get { return banFile; } }

        /// <summary>
        /// The duration after the last failed login attempt that the count is reset back to 0.
        /// </summary>
        public TimeSpan ExpireTime { get { return expireTime; } }
        
        /// <summary>
        /// Interval of time to do house-keeping chores like un-banning ip addresses
        /// </summary>
        public TimeSpan CycleTime { get { return cycleTime; } }

        /// <summary>
        /// Rule name for Windows Firewall
        /// </summary>
        public string RuleName { get { return ruleName; } }

		/// <summary>
		/// Freeswitch Log File Path
		/// </summary>
		public string FreeswitchLogFilePath { get { return freeswitchLogFilePath; } }
		public int ReadFreeswitchLogTimeout { get { return readFreeswitchLogTimeout; } }

        /// <summary>
        /// Expressions to block
        /// </summary>
        public ExpressionsToBlock Expressions { get { return expressions; } }

        /// <summary>
        /// True to clear and unband ip addresses in the ban file when the service restarts, false otherwise
        /// </summary>
        public bool BanFileClearOnRestart { get { return banFileClearOnRestart; } }

        /// <summary>
        /// Black list of ips as a comma separated string
        /// </summary>
        public string BlackList { get { return string.Join(",", blackList); } }

        /// <summary>
        /// Black list regex
        /// </summary>
        public string BlackListRegex { get { return (blackListRegex == null ? string.Empty : blackListRegex.ToString()); } }

        /// <summary>
        /// White list of ips as a comma separated string
        /// </summary>
        public string WhiteList { get { return string.Join(",", whiteList); } }

        /// <summary>
        /// White list regex
        /// </summary>
        public string WhiteListRegex { get { return (whiteListRegex == null ? string.Empty : whiteListRegex.ToString()); } }

        /// <summary>
        /// Allowed user names as a comma separated string
        /// </summary>
        public string AllowedUserNames { get { return string.Join(",", allowedUserNames); } }

		
	}
}
