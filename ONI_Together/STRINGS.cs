
using Steamworks;

namespace ONI_Together
{
	internal class STRINGS
	{
		public class UI
		{
			public class CONFIGURATION
			{
				public class HEADERS
				{
					public static LocString A_HOST_SETTINGS = "Host Settings";
					public static LocString B_CLIENT_SETTINGS = "Client Settings";
					public static LocString C_NETWORK_SETTINGS = "General Network Settings";
                    public static LocString D_CURSOR_SETTINGS = "Cursor Settings";
					public static LocString E_MISC_SETTINGS = "Misc Settings";

					// Additional lobby options header
                    public static LocString SERVER_SETTINGS = "Server Settings";
                }

                public class TITLES
                {
                    public class HOST_SETTINGS
                    {
                        public static LocString MAX_MESSAGES_PER_POLL = "Max Messages Per Poll";
						public static LocString SAVE_FILE_TRANSFER_CHUNK = "Save Chunk Size (KB)";

						public class SERVER_SETTINGS
						{
							public static LocString HARD_SYNC_AT_CYCLE_START = "Hard Sync On Cycle Start";
							public static LocString TIMEOUT_SECONDS = "Connection Timeout (seconds)";
							public static LocString PAUSE_SIM_ON_PLAYER_DISCONNECT = "Pause Simulation On Player Disconnect";
						}
                    }

                    public class CLIENT_SETTINGS
                    {
                        public static LocString MAX_MESSAGES_PER_POLL = "Max Messages Per Poll";
                        public static LocString TIMEOUT_SECONDS = "Connection Timeout (seconds)";
                    }

					public class NETWORK_SETTINGS
					{
						public static LocString ENABLE_PACKET_QUEUE = "Enable Packet Queue";
						public static LocString MAX_PACKETS_PER_SECOND = "Max Packets Per Second";
                        public static LocString BYPASS_PROTOCOL_CHECKS = "Bypass Protocol Checks";
                    }

                    public class CURSOR_SETTINGS
                    {
						public static LocString RANDOM_COLOR = "Use Random Color";
						public static LocString CURSOR_COLOR = "Cursor Color";
                    }

                    public class MISC_SETTINGS
                    {
                        public static LocString PUFT_LOADINGSCREEN = "Use Puft Loading Icon";
                        public static LocString LOADINGSCREEN_COLOR = "Use Custom Loading Screen Color";
                    }
                }

                public class TOOLTIPS
                {
                    public class HOST_SETTINGS
                    {
                        public static LocString MAX_MESSAGES_PER_POLL = "STEAMWORKS ONLY:\nMaximum number of incoming Steam networking messages processed per poll tick on the server.\nHigher values reduce latency spikes under heavy traffic but may increase CPU usage per frame.\nLower values improve frame stability but can increase message backlog and delay processing during bursts.\n\nRecommended range: 32–128 (default: 128)";
						public static LocString SAVE_FILE_TRANSFER_CHUNK = "Size (in kilobytes) of each data chunk used when transferring save files over the network.\nSmaller values reduce bandwidth spikes and improve reliability on unstable connections but increase transfer time due to more packets.\nLarger values speed up transfers but may cause higher bandwidth bursts or fragmentation.\n\nRecommended range: 16–256 KB (default: 256 KB).";

                        public class SERVER_SETTINGS
                        {
                            public static LocString HARD_SYNC_AT_CYCLE_START = "Perform a hard sync at the start of every new cycle\n\nDoes not use up your one hard sync per cycle";
                            public static LocString TIMEOUT_SECONDS = "How long the server waits (in seconds) for a response from a connecting or loading client before timing out.\nIncrease this if your friends take a long time to load into the game.\n\nMinimum: 30. (default: 30)";
                            public static LocString PAUSE_SIM_ON_PLAYER_DISCONNECT = "Automatically pauses the simulation when a player disconnects from the server.";
                        }
                    }

                    public class CLIENT_SETTINGS
                    {
						public static LocString MAX_MESSAGES_PER_POLL = "STEAMWORKS ONLY:\nMaximum number of incoming Steam networking messages processed per poll tick on the client.\nHigher values reduce input and state update latency under heavy network load but may increase per-frame CPU usage.\nLower values improve frame stability but can cause message backlog and delayed processing during traffic spikes.\n\nRecommended range: 8–64 (default: 16)";
						public static LocString TIMEOUT_SECONDS = "How long the client waits (in seconds) for a response from the server before timing out.\nIncrease this if you are timing out while trying to connect.\n\nMinimum: 30. (default: 30)";

                    }

                    public class NETWORK_SETTINGS
                    {
                        public static LocString ENABLE_PACKET_QUEUE = "When enabled, outgoing packets are queued and sent at a limited rate per frame to prevent network saturation.\nWhen disabled, all packets are sent immediately without throttling.";
                        public static LocString MAX_PACKETS_PER_SECOND = "Maximum number of packets to send per second per connection when the packet queue is enabled.\nExcess packets are buffered and sent in subsequent frames.\n\nRanges: (500 - 1000)";
                        public static LocString BYPASS_PROTOCOL_CHECKS = "Ignores protocol version, packet registry fingerprint, and mod version mismatches during connection handshake.\n\nUse this only for cross-platform testing between Debug and Release builds.";
                    }

                    public class CURSOR_SETTINGS
                    {
						public static LocString RANDOM_COLOR = "Select a random color for your cursor during online sessions.";
						public static LocString CURSOR_COLOR = "The color of your player cursor for yourself during an online session\n(visible to other players in the session)";
                    }

                    public class MISC_SETTINGS
                    {
						public static LocString PUFT_LOADINGSCREEN = "Override the loading icon with a Puft.";
						public static LocString LOADINGSCREEN_COLOR = "Override the purple background of the loading screen with turquoise.";
                    }
                }
            }

            

            public class PROTOCOL
            {
                public static LocString NO_METADATA = "Peer is running a build without protocol metadata.";
                public static LocString PROTOCOL_MISMATCH = "Protocol mismatch. Host={0}, Peer={1}.";
                public static LocString PACKET_REGISTRY_MISMATCH = "Packet registry mismatch. Host={0}, Peer={1}.";
                public static LocString MOD_VERSION_MISMATCH = "Mod version mismatch. Host={0}, Peer={1}.";
                public static LocString GAME_BUILD_MISMATCH = "Game build mismatch. Host={0}, Peer={1}.";
                public static LocString MOD_BUILD_MISMATCH = "Mod DLL fingerprint mismatch. Install the exact same build on every peer.";
				public static LocString DLC_MISMATCH = "Active DLC mismatch. Host={0}; Peer={1}. Enable the same DLCs on every peer.";
                public static LocString INCOMPATIBLE = "Peer is running an incompatible build.";

				public class VALIDATION
				{
					public static LocString TITLE = "Protocol mismatch";
					public static LocString NO_METADATA = "The host is running a build without protocol metadata. Update both peers to the same build.";
					public static LocString REJECTED = "The host rejected this client due to an incompatible network protocol.";
					public static LocString PROTOCOL_MISMATCH = "Host protocol={0}, client protocol={1}.";
                    public static LocString FINGERPRINT_MISMATCH = "Host packet fingerprint={0}, client fingerprint={1}.";
                    public static LocString GAME_BUILD_MISMATCH = "Host game build={0}, client game build={1}.";
                    public static LocString MOD_BUILD_MISMATCH = "The host and client mod DLLs are not byte-identical.";
                }
            }

            public class SERVERBROWSER
			{
				public static LocString MULTIPLAYER_SESSION_TITLE = "Multiplayer Session";
				public static LocString LOBBY_CODE = "Lobby Code:";
				public static LocString COPY = "Copy";
				public static LocString COPIED = "Copied!";
				public static LocString CONNECTED_PLAYERS = "Connected Players: {0}";
				public static LocString TITLE = "Public Lobby Browser";


				public class HEADERS
				{
					public static LocString COLONY = "Colony";
					public static LocString HOST = "Host";
					public static LocString PLAYERS = "Players";
					public static LocString CYCLE = "Cycle";
					public static LocString DUPES = "Dupes";
					public static LocString PING = "Ping";
				}

				public static LocString JOIN_BY_CODE = "Join by Code";
				public static LocString BACK = "Back";
				public static LocString LOADING_LOBBIES = "Loading Lobbies...";
				public static LocString NO_PUBLIC_LOBBIES_FOUND = "No public lobbies found. Try hosting your own!";
				public static LocString FOUND_X_LOBBIES = "Found {0} joinable lobby(s)";
				public static LocString JOIN_BUTTON = "Join";
				public static LocString FRIEND_ONLY = "Friends Only";
				public static LocString PASSWORD_REQUIRED = "Password Required";
				public static LocString PASSWORD_INCORRECT = "Incorrect password";
				public static LocString CANCEL = "Cancel";
				public static LocString REFRESH = "Refresh";
				public static LocString SEARCH = "{0} Search...";
			}

			public class MODCOMPATIBILITY
			{
				public class MANAGER
				{
					public static LocString WARNING_MOD_HASH_MISMATCH = "Mod hash mismatch - possible mod order difference";
					public static LocString MISSING_MODS = "{0} missing mods";
					public static LocString EXTRA_MODS = "{0} extra mods";
					public static LocString VERSION_MISMATCHES = "{0} version mismatches";
					public static LocString MOD_INCOMPATIBILITY = "Mod incompatibility: {0}";
				}

				public class COMPATIBILITYDIALOG
				{
					public static LocString HEADER_TITLE = "Mod Compatibility Error";
					public static LocString HEADER_TITLE_ERROR = "Mod Compatibility Error: {0}";
					public static LocString DISABLE_MODS = "DISABLED MODS (enable these):";
					public static LocString MISSING_MODS = "MISSING MODS (install these):";
					public static LocString EXTRA_MODS = "EXTRA MODS (disable these):";
					public static LocString VERSION_MISMATCHES = "VERSION MISMATCHES (update these):";
					public static LocString YOU_HAVE_EXTRA_MODS_ALLOWED = "You have extra mods (this is allowed):";
					public static LocString EXTRA_MODS_YOU_HAVE = "EXTRA MODS (you have these):";
					public static LocString ENSURE_HOST_CONFIG = "Please ensure your mods match the host's configuration.";

					public static LocString BUTTON_INSTALL = "Install";
					public static LocString BUTTON_INSTALL_ALL = "Install All";
					public static LocString BUTTON_VIEW = "View";
					public static LocString BUTTON_UPDATE = "Update";
					public static LocString BUTTON_OK = "Ok";
					public static LocString BUTTON_ENABLE = "Enable";
					public static LocString BUTTON_ENABLE_ALL = "Enable All";
					public static LocString BUTTON_CLOSE = "Close";

					public static LocString INSTRUCTIONS = "Install/disable the required mods, then try connecting again.";
					public static LocString CONNECTION_ALLOWED = "Connection allowed. Your extra mods shouldn't cause issues.";

					public static LocString CONNECTION_ALLOWED_HEADER = "Connection Allowed";

					public static LocString YOU_HAVE_EXTRA_MODS = "You have {0} extra mod(s) that the host doesn't have.";
					public static LocString ALLOWED_SHOULDNT_CAUSE_ISSUES = "This is allowed and shouldn't cause issues.";
					public static LocString YOUR_EXTRA_MODS = "Your extra mods:";
					public static LocString MOD_ENTRY = "• {0}";

					public static LocString MODS_ENABLED_SUCCESSFULLY = "Mods enabled successfully!\nPlease restart the game for changes to take effect.";
					public static LocString ALL_MODS_ENABLED = "All mods have been enabled!";
					public static LocString RESTART = "Close this window to restart the game and apply changes.";
					public static LocString MODS_CHANGED = "Mods have been enabled. Close this window to restart the game.";

					public static LocString INSTALL_DISABLE_RECONNECT = "Install/disable mods, then reconnect.\nPress ESC to close.";
				}

				public class COMPATIBILITYRESULT
				{
					public static LocString APPROVED = "Approved";
					public static LocString APPROVED_EXTRA = "Compatible (client has {0} extra mod(s))";
					public static LocString APPROVED_COMPATIBLE = "Compatible";
					public static LocString REJECT_NOT_A_HOST = "Internal error: not a host";
					public static LocString REJECT_GAME_VERSION_MISMATCH = "Game version mismatch: Host={0}, Client={1}";
					public static LocString COMPATIBILITY_ISCOMPATIBLE = "Compatible: {0}";
					public static LocString COMPATIBILITY_INCOMPATIBLE = "Incompatible: {0} ({1})";
					public static LocString COMPATIBILITY_MISSING = "{0} missing";
					public static LocString COMPATIBILITY_EXTRA = "{0} extra";
					public static LocString COMPATIBILITY_MISMATCH = "{0} version mismatches";
					public static LocString REJECT_VERIFICATION_ERROR = "Verification error: {0}";
				}

			}
			public class HOSTLOBBYCONFIGSCREEN
			{
				public static LocString HOST_LOBBY_SETTINGS = "Host Lobby Settings";
				public static LocString LOBBY_VISIBILITY = "Lobby Visibility:";
				public static LocString LOBBY_VISIBILITY_PUBLIC = "Public";
				public static LocString LOBBY_VISIBILITY_FRIENDSONLY = "Friends Only";
				public static LocString PASSWORD_TITLE = "Password (optional):";
				public static LocString PASSWORD_NOTE = "Leave empty for no password";
				public static LocString CONTINUE = "Continue";
				public static LocString CANCEL = "Cancel";
				public static LocString LOBBY_SIZE = "Lobby Size:";
			}

			public class JOINBYDIALOGMENU
			{
				public static LocString JOIN_BY_CODE = "Join By Code";
				public static LocString ENTER_LOBBY_CODE = "Enter Lobby Code:";
				public static LocString DEFAULT_CODE = "XXXX-XXXX";
				public static LocString PASSWORD_REQUIRED = "Password Required:";
				public static LocString ENTER_PASSWORD = "Enter password";
				public static LocString JOIN = "Join";
				public static LocString CANCEL = "Cancel";

				public static LocString ERR_ENTER_CODE = "Please enter a lobby code";
				public static LocString ERR_INVALID_CODE = "Invalid lobby code format";
				public static LocString ERR_PARSE_CODE_FAILED = "Could not parse lobby code";
				public static LocString ERR_LOBBY_DATA_FAILED = "Could not read lobby data";

				public static LocString CHECKING_LOBBY = "Checking lobby...";
				public static LocString LOBBY_REQUIRES_PASSWORD = "This lobby requires a password";

				public static LocString VALIDATE_ENTER_PASSWORD = "Please enter the password";
				public static LocString VALIDATE_ERR_INCORRECT_PASSWORD = "Incorrect password";

				public static LocString JOINING = "Joining...";
			}

			// Main Menu multiplayer menu
			public class MULTIPLAYERMENU
			{
				public static LocString TITLE = "Multiplayer";
				public static LocString HOST_WORLD = "Host World";
				public static LocString HOST_WORLD_FLAVOR = "Select a save to host";
				public static LocString BROWSE_LOBBIES = "Browse Lobbies";
				public static LocString BROWSE_LOBBIES_FLAVOR = "Find public games to join";
				public static LocString JOIN_BY_CODE = "Join by code";
				public static LocString JOIN_BY_CODE_FLAVOR = "Enter a lobby code";
				public static LocString JOIN_BY_STEAM = "Join via Steam";
				public static LocString JOIN_BY_STEAM_FLAVOR = "Find friends playing";
				public static LocString BACK = "Back";
			}

			public class MAINMENU
			{
				public static LocString JOINGAME = "JOIN GAME";
				public static LocString DISCORD_INFO = "Join ONI Together\non Discord";

				public class MULTIPLAYER
				{
					public static LocString LABEL = "MULTIPLAYER";
				}
			}
			public class PAUSESCREEN
			{
				public class MULTIPLAYER
				{
					public static LocString LABEL = "Multiplayer";
				}

				//maybe add tooltips to these later in some way?
				public class HOSTGAME
				{
					public static LocString LABEL = "Host Game";
					//e.g:
					//public static LocString TOOLTIP = "Host your current game as a multiplayer session";
				}
				public class INVITE
				{
					public static LocString LABEL = "Invite Friends";
				}
				public class DOHARDSYNC
				{
					public static LocString LABEL = "Perform Hard Sync";
				}
				public class HARDSYNCNOTAVAILABLE
				{
					public static LocString LABEL = "Already hard synced this cycle!";
				}
				public class ENDSESSION
				{
					public static LocString LABEL = "End Session";
				}
				public class LEAVESESSION
				{
					public static LocString LABEL = "Leave Session";
				}
			}
			public class MP_CHATWINDOW
			{
				public static LocString CHAT_INITIALIZED = "<color=yellow>[System]</color> Chat initialized.";
				public static LocString CHAT_CLIENT_REJECTED = "<color=red>[System]</color> {0} was rejected due to mod incompatibility: {1}";
				public static LocString CHAT_CLIENT_JOINED = "<color=yellow>[System]</color> <b>{0}</b> joined the game.";
				public static LocString CHAT_CLIENT_LEFT = "<color=yellow>[System]</color> <b>{0}</b> left the game.";
				public static LocString CHAT_CLIENT_FAILED = "<color=red>[System]</color>{0} failed to connect to the server.";

                public static LocString CHAT_SERVER_STARTED = "<color=yellow>[System]</color><color=green> Started server over {0}</color>";
				public static LocString CHAT_SERVER_STOPPED = "<color=yellow>[System]</color><color=green> Stopped {0} server</color>";

                public class RESIZE
				{
					public static LocString EXPAND = "Chat (+)";
					public static LocString RETRACT = "Chat (-)";
				}
			}
			public class MP_OVERLAY
			{
				public class HOST
				{
					public static LocString STARTINGHOSTING = "Hosting game...";
					public static LocString SEND_SAVE_FILE = "Sending save file to a client!\nPlease wait...";
                }

				public class CLIENT
				{
					public static LocString DOWNLOADING_GAME = "Downloading world: {0}%";
					public static LocString DEFAULT_LOST_CONNECTION = "";
					public static LocString MENU_LOST_CONNECTION = "Connection to the host was lost!";
                    public static LocString LOST_CONNECTION = "Connection to the host was lost: {0}\n{1}";

					public class STEAMWORKS
					{
						public static LocString HOST_DISCONNECTED = "Host disconnected";
                        public static LocString HOST_DISCONNECTED_DESC = "The host has ended the game or returned to the menu.";

						public static LocString LOCAL_PROBLEM = "Connection lost";
                        public static LocString LOCAL_PROBLEM_DESC = "A network error occurred or the connection timed out.";

                        public static LocString KICKED = "Kicked from server";
                        public static LocString KICKED_DESC = "You were removed from the session.";

                        public static LocString UNKNOWN = "Disconnected";
                        public static LocString UNKNOWN_DESC = "An unknown network error occurred.";
                    }

                    public class RIPTIDE
					{
                        public static LocString CONNECTION_FAILED = "Connection failed";
                        public static LocString CONNECTION_FAILED_DESC = "Could not establish a connection to the server.";

                        public static LocString CONNECTION_REJECTED = "Connection rejected";
                        public static LocString CONNECTION_REJECTED_DESC = "The server refused the connection.";

                        public static LocString NETWORK_ERROR = "Network error";
                        public static LocString NETWORK_ERROR_DESC = "A network error occurred.";

                        public static LocString CONNECTION_TIMED_OUT = "Connection timed out";
                        public static LocString CONNECTION_TIMED_OUT_DESC = "The server did not respond in time.";

                        public static LocString KICKED = "Kicked from server";
                        public static LocString KICKED_DESC = "You were removed from the session.";

                        public static LocString SERVER_CLOSED = "Server closed";
                        public static LocString SERVER_CLOSED_DESC = "The host has stopped the server.";

                        public static LocString CONNECTION_UNSTABLE = "Connection unstable";
                        public static LocString CONNECTION_UNSTABLE_DESC = "The connection quality was too poor to continue.";

                        public static LocString UNKNOWN = "Disconnected";
                        public static LocString UNKNOWN_DESC = "An unknown network error occurred.";
                    }

					public static LocString MISSING_SAVE_FILE = "Downloaded save file not found.";
					public static LocString CONNECTING_TO_HOST = "Connecting to {0}!";
					public static LocString WAITING_FOR_PLAYER = "Waiting for {0}...";

					public static LocString DOWNLOADING_SAVE_FILE = "Downloading Save File\n\n{0} {1}%\n({2}/{3} chunks)";
					public static LocString TCP_DOWNLOADING_SAVE_FILE = "Downloading LAN Save File\n\n{0} {1}%\n({2} remaining)";
					public static LocString DOWNLOAD_COMPLETE = "Download Complete!\n\n{0} 100%\n({1}/{2} chunks)\n\nLoading world...";

					public static LocString CONNECTION_FAILED = "Failed to connect to host.";


                    public class NETWORKINDICATORS
					{
						public static LocString DEGRADED_JITTER = "Network jitter is elevated";
                        public static LocString DEGRADED_LATENCY = "Latency is elevated";
                        public static LocString DEGRADED_PACKETLOSS = "Packet loss detected";
                        public static LocString DEGRADED_SERVERPERFORMANCE = "Server performance is reduced";

                        public static LocString BAD_JITTER = "Network jitter is very high";
                        public static LocString BAD_LATENCY = "Latency is very high";
                        public static LocString BAD_PACKETLOSS = "Severe packet loss detected";
                        public static LocString BAD_SERVERPERFORMANCE = "Server performance is critically low";
                    }

				}
				public class SYNC
				{
					public static LocString HARDSYNC_INPROGRESS = "Hard sync in progress!";
					public static LocString FINALIZING_SYNC = "All players are ready!\nPlease wait...";
					public static LocString WAITING_FOR_PLAYERS_SYNC = "Waiting for players ({0}/{1} ready)...\n";

					// New world sync menu
					public static LocString CLIENT_SYNC_PROGRESS = "Client Sync Progress";
					public static LocString CLIENT_PROGRESS = "{0}: {1} {2}%";
					public static LocString CLIENT_CHUNK_SYNC_DATA = "({0}/{1})";
					public static LocString CLIENT_SYNC_COMPLETE = "[COMPLETE]";
					public static LocString ALL_CLIENTS_SYNCED = "ALL CLIENTS SYNCHRONIZED!\nWAITING FOR THEM TO LOAD!";

					public static LocString PROGRESS_BAR_FILLED = "|";
					public static LocString PROGRESS_BAR_EMPTY = " ";
					public static LocString PROGRESS_BAR = "[{0}]";

					public class READYSTATE
					{
						public static LocString READY = "Ready";
						public static LocString UNREADY = "Loading";
						public static LocString UNKNOWN = "Unknown";
					}

					public class DLCSYNC
					{
						public static LocString WRONGDLC_BASEGAME = "Server requires Spaced Out, cannot join without SpacedOut active!";
						public static LocString WRONGDLC_SPACEDOUT = "Server requires Base Game, cannot join with Spaced Out active!";
						public static LocString WRONGDLC_LISTHEADER = "Server requires the following DLCs which are not installed or active:";
					}
					public class MODSYNC
					{
						public static LocString TITLE = "Mod Differences Detected!";
						public static LocString TEXT = "The game you want to join has different mods active than you.\nDo you want to synchronize your mods?";
						public static LocString TODISABLE = "• {0} mods will be disabled";
						public static LocString TOENABLE = "• {0} mods will be enabled";
						public static LocString MISSING = "• {0} mods are missing and will be subscribed to.";
						public static LocString VERSION_TEXT = "The enabled mod versions or local mod set do not exactly match the host. Connecting would desynchronize the simulation.";
						public static LocString HOST_ONLY = "• {0} host mod revisions are missing locally";
						public static LocString CLIENT_ONLY = "• {0} local mod revisions are not active on the host";

						public static LocString CONFIRM_SYNC = "Yes, synchronize my mods and restart";
						public static LocString CANCEL = "No, go back to the main menu";
					}
				}
			}

			public static LocString WORK_IN_PROGRESS = "This feature is currently not finished and will be made usable in a future update.";
			///Unity Ui big composite screen, these get automatically applied, and are auto generated from the unity project
			/// <summary>
			public class FRIENDSONLYMODE
			{
				public static LocString LOBBY_VISIBILITY_PUBLIC = "Public";
				public static LocString LOBBY_VISIBILITY_FRIENDSONLY = "Friends Only";
			}

			public class MP_SCREEN
			{
				public class MAINMENU
				{
					public static LocString HOSTINGTITLE = "Hosting";
					public class HOSTGAMEBUTTON
					{
						public static LocString TEXT = "Host Game";
					}
					public static LocString JOININGTITLE = "Joining";
					public class JOINVIABUTTONS
					{
						public class STEAM
						{
							public static LocString TEXT = "Steam";
						}
						public class CODE
						{
							public static LocString TEXT = "Code";
						}
						public class LAN
						{
							public static LocString TEXT = "LAN";
						}
					}
					public class STEAMJOIN
					{
						public class JOINVIASTEAM
						{
							public static LocString TEXT = "Join via Steam";
						}
						public class OPENLOBBYLISTBUTTON
						{
							public static LocString TEXT = "Open Lobby Browser";
						}
					}
					public class LOBBYCODEJOIN
					{
						public class INPUT
						{
							public class TEXTAREA
							{
								public static LocString PLACEHOLDER = "Enter Lobby Code...";
								public static LocString TEXT = "​";
							}
						}
						public class JOINWITHCODEBUTTON
						{
							public static LocString TEXT = "Join with Code";
						}
					}
					public class LANJOIN
					{
						public class INPUTS
						{
							public class IPINPUT
							{
								public class TEXTAREA
								{
									public static LocString PLACEHOLDER = "Enter IP address...";
									public static LocString TEXT = "​";
								}
							}
							public class PORT
							{
								public class TEXTAREA
								{
									public static LocString PLACEHOLDER = "Enter Port...";
									public static LocString TEXT = "​";
								}
							}
						}
						public class JOINLANBUTTON
						{
							public static LocString TEXT = "Join LAN address";
						}
					}
					public class CANCEL
					{
						public static LocString TEXT = "Cancel";
					}
				}
				public class HOSTMENU
				{
					public static LocString TITLE = "Host Lobby Settings";
					public class HOSTVIABUTTONS
					{
						public class STEAM
						{
							public static LocString TEXT = "Steam Session";
						}
						public class LAN
						{
							public static LocString TEXT = "LAN Session";
						}
					}
					public class LOBBYSIZE
					{
						public static LocString LABEL = "Lobby Size:";
						public class LOBBYSIZEINPUT
						{
							public class TEXTAREA
							{
								public static LocString PLACEHOLDER = "Enter Lobby Size...";
								public static LocString TEXT = "4​";
							}
						}
					}
					public class STEAMHOSTING
					{
						public class FRIENDSONLY
						{
							public static LocString LABEL = "Private Lobby:";
							public static LocString STATE = "Friends Only";
						}
						public class PASSWORD
						{
							public static LocString PASSWORDTITLE = "Password (optional):";
						}
						public class PASSWORDINPUT
						{
							public class TEXTAREA
							{
								public static LocString PLACEHOLDER = "Leave empty for no password";
								public static LocString TEXT = "​";
							}
						}
					}
					public class LANHOSTING
					{
						public class IPTARGET
						{
							public static LocString LABEL = "Host IP:";
							public class INPUT
							{
								public class TEXTAREA
								{
									public static LocString PLACEHOLDER = "Enter IP address...";
									public static LocString TEXT = "127.0.0.1​";
								}
							}
						}
						public class PORT
						{
							public static LocString LABEL = "Port";
							public class INPUT
							{
								public class TEXTAREA
								{
									public static LocString PLACEHOLDER = "Enter Port...";
									public static LocString TEXT = "8080​";
								}
							}
						}
					}
					public class ADDITIONALSETTINGS
					{
						public static LocString TEXT = "Additional Lobby Settings";
					}
					public class BUTTONS
					{
						public class CANCEL
						{
							public static LocString TEXT = "Cancel";
						}
						public class STARTHOSTING
						{
							public static LocString TEXT = "Start Hosting";
						}
					}
				}
				public class LOBBYLIST
				{
					public static LocString TITLE = "Public Lobby Browser";
					public class SEARCHBAR
					{
						public class INPUT
						{
							public class TEXTAREA
							{
								public static LocString PLACEHOLDER = "Search Lobbies...";
								public static LocString TEXT = "​";
							}
						}
					}
					public class INFO
					{
						public static LocString WORLD = "World Name";
						public static LocString HOST = "Host";
						public static LocString PLAYERS = "Players";
						public static LocString CYCLE = "Cycle";
						public static LocString DUPES = "Dupes";
						public static LocString PING = "Ping";
					}
					public class SCROLLAREA
					{
						public class CONTENT
						{
							public class NOLOBBIES
							{
								public static LocString LABEL = "No public lobbies found. Try hosting your own!";
							}
							public class ENTRYPREFAB
							{
								public class JOINLOBBYBUTTON
								{
									public static LocString TEXT = "Join";
								}
							}
						}
					}
				}				
				public class TOPBAR
				{
					public static LocString LABEL = "Multiplayer";
				}
				public class ADDITIONALHOSTSETTINGS
				{
					public static LocString TITLE = "Additional Lobby Settings";
				}
			}

			///Unity UI password input screen, these get automatically applied
			public class MP_PASSWORD_DIALOGUE
			{
				public class HOSTMENU
				{
					public static LocString TITLE = "Password Required!";
					public static LocString PASSWORDTITLE = "Enter password of Lobby:";
					public static LocString PASSWORD_INCORRECT = "Incorrect password!";
					public class PASSWORDINPUT
					{
						public class TEXTAREA
						{
							public static LocString PLACEHOLDER = "Enter lobby password...";
							public static LocString TEXT = "";
						}
					}
					public class BUTTONS
					{
						public class CANCEL
						{
							public static LocString TEXT = "Cancel";
						}
						public class CONFIRM
						{
							public static LocString TEXT = "Confirm";
						}
					}
				}
				public class TOPBAR
				{
					public static LocString LABEL = "Joining password protected lobby";
				}
			}

			///Unity UI for multiplayer lobby state screen

			public class MP_LOBBY_STATE_DIALOGUE
			{
				public class TOPBAR
				{
					public static LocString LABEL = "Multiplayer Session";
				}
				public static LocString LOBBYCODETITLE = "Lobby Code:";
				public class INVITEFRIENDS
				{
					public static LocString TEXT = "Invite Friends";
				}
				public class ENDSESSION
				{
					public static LocString TEXT = "End Session";
				}
			}
		}
	}
}
