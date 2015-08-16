﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;

using static LanguageExt.Prelude;
using static LanguageExt.Process;

namespace LanguageExt
{
    public class ProcessHub : Hub
    {
        static object sync = new object();
        static ProcessId processHub;
        static Set<string> processWhitelist;
        static Set<string> processBlacklist;

        /// <summary>
        /// Process whitelist
        /// Prevents javascript process access to server processes unless they're on the whitelist
        /// NOTE: The blacklist and whitelist is an either/or situation, you must decide on whether to
        /// white or blacklist a set of processes.
        /// </summary>
        /// <param name="processes">Processes to add to the whitelist</param>
        public static void AddToProcessWhitelist(IEnumerable<ProcessId> processes)
        {
            lock(sync)
            {
                Set<string> set = Set.createRange(processes.Map(x => x.Path).Distinct());
                processWhitelist = processWhitelist == null
                    ? processWhitelist = set
                    : processWhitelist.Union(set);

                processBlacklist = null;
            }
        }

        /// <summary>
        /// Process whitelist
        /// Prevents javascript process access to server processes unless they're on the whitelist
        /// NOTE: The blacklist and whitelist is an either/or situation, you must decide on whether to
        /// white or blacklist a set of processes.
        /// </summary>
        /// <param name="process">Process to add to the whitelist</param>
        public static void AddToProcessWhitelist(ProcessId process)
        {
            AddToProcessWhitelist( new[] { process } );
        }

        /// <summary>
        /// Process blacklist
        /// Prevents javascript process access to server processes unless they're not on the blacklist
        /// NOTE: The blacklist and whitelist is an either/or situation, you must decide on whether to
        /// white or blacklist a set of processes.
        /// </summary>
        /// <param name="processes">Processes to add to the whitelist</param>
        public static void AddToProcessBlacklist(IEnumerable<ProcessId> processes)
        {
            lock (sync)
            {
                Set<string> set = Set.createRange(processes.Map(x => x.Path).Distinct());
                processBlacklist = processBlacklist == null
                    ? processBlacklist = set
                    : processBlacklist.Union(set);

                processWhitelist = null;
            }
        }

        /// <summary>
        /// Process blacklist
        /// Prevents javascript process access to server processes unless they're not on the blacklist
        /// NOTE: The blacklist and whitelist is an either/or situation, you must decide on whether to
        /// white or blacklist a set of processes.
        /// </summary>
        /// <param name="process">Process to add to the whitelist</param>
        public static void AddToProcessBlacklist(ProcessId process)
        {
            AddToProcessBlacklist(new[] { process });
        }

        /// <summary>
        /// Remove process from blacklist
        /// </summary>
        public static void RemoveFromProcessBlacklist(IEnumerable<ProcessId> processes)
        {
            if (processBlacklist != null)
            {
                processBlacklist = Set.difference(processBlacklist, Set.createRange(processes.Map(p => p.Path).Distinct()));
            }
        }

        /// <summary>
        /// Remove process from blacklist
        /// </summary>
        public static void RemoveFromProcessBlacklist(ProcessId process)
        {
            if (processBlacklist != null)
            {
                processBlacklist = processBlacklist.Remove(process.Path);
            }
        }

        /// <summary>
        /// Remove process from whitelist
        /// </summary>
        public static void RemoveFromProcessWhitelist(IEnumerable<ProcessId> processes)
        {
            if (processWhitelist != null)
            {
                processWhitelist = Set.difference(processWhitelist, Set.createRange(processes.Map(p => p.Path).Distinct()));
            }
        }

        /// <summary>
        /// Remove process from whitelist
        /// </summary>
        public static void RemoveFromProcessWhitelist(ProcessId process)
        {
            if (processWhitelist != null)
            {
                processWhitelist = processWhitelist.Remove(process.Path);
            }
        }

        /// <summary>
        /// Tell method called by the javascript process system.  Do not used it directly.
        /// </summary>
        public void Tell(string pid, string message, string sender)
        {
            Bouncer(pid, () =>
            {
                var spid = FixRootName(pid);
                tell(Root["js"], new InboundRelayMsg(Context.ConnectionId, message, spid, sender, false), FixSender(sender));
            });
        }

        private static ProcessId FixRootName(string pid)
        {
            ProcessId cpid = pid;
            return cpid.Take(1).GetName().Value == "root"
                ? Root.Append(cpid.Skip(1))
                : cpid;
        }

        private static ProcessId FixSender(string sender) =>
            String.IsNullOrWhiteSpace(sender) || sender == "/no-sender"
                ? ProcessId.NoSender
                : sender;

        private static void Bouncer(string pid, Action f)
        {
            if (processWhitelist != null)
            {
                if (!processWhitelist.Contains(pid)) return;
            }
            if (processBlacklist != null)
            {
                if (processBlacklist.Contains(pid)) return;
            }
            f();
        }

        private ProcessId EnsureProcessHub()
        {
            if (processHub.IsValid)
            {
                return processHub;
            }
            else
            {
                lock (sync)
                {
                    if (processHub.IsValid) return processHub;
                    processHub = spawn<OutboundRelayMsg>("process-hub-js", msg =>
                    {
                        var conns = new List<string>();
                        conns.Add(msg.ConnectionId);
                        GlobalHost.ConnectionManager.GetHubContext<ProcessHub>().Clients.Clients(conns).onMessage(msg.Message);
                    });
                    return processHub;
                }
            }
        }

        private void Connect()
        {
            EnsureProcessHub();
            tell(Root["js"], new RelayMsg(RelayMsg.MsgTag.Connected, Context.ConnectionId, ProcessId.NoSender, ProcessId.NoSender, false));
        }

        public override Task OnConnected()
        {
            Connect();
            return base.OnConnected();
        }

        public override Task OnReconnected()
        {
            EnsureProcessHub();
            tell(Root["js"], new RelayMsg(RelayMsg.MsgTag.Connected, Context.ConnectionId, ProcessId.NoSender, ProcessId.NoSender, false));
            return base.OnReconnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            tell(Root["js"], new RelayMsg(RelayMsg.MsgTag.Disconnected, Context.ConnectionId, ProcessId.NoSender, ProcessId.NoSender, false));
            return base.OnDisconnected(stopCalled);
        }
    }
}
