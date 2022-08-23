﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RageCoop.Core.Scripting;
using RageCoop.Core;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using System.Reflection;
using McMaster.NETCore.Plugins;
namespace RageCoop.Server.Scripting
{
	internal class Resources
	{
		public Dictionary<string,ServerResource> LoadedResources=new();
		private readonly Server Server;
		private readonly Logger Logger;
		public bool IsLoaded { get; private set; } = false;
		public Resources(Server server)
		{
			Server = server;
			Logger=server.Logger;
		}
		private Dictionary<string,Func<Stream>> ClientResources=new();
		private Dictionary<string,Func<Stream>> ResourceStreams=new();
		private List<MemoryStream> MemStreams = new();
		public void LoadAll()
		{
			// Packages
			{
				var path = Path.Combine("Resources", "Packages");
				Directory.CreateDirectory(path);
				foreach (var pkg in Directory.GetFiles(path,"*.respkg",SearchOption.AllDirectories))
                {
					Logger?.Debug($"Adding resourece from package \"{Path.GetFileNameWithoutExtension(pkg)}\"");
					var pkgZip = new ZipFile(pkg);
					foreach (ZipEntry e in pkgZip)
					{
						if (!e.IsFile) { continue; }
						if (e.Name.StartsWith("Client") && e.Name.EndsWith(".res"))
						{
							var stream = pkgZip.GetInputStream(e).ToMemStream();
							MemStreams.Add(stream);
							ClientResources.Add(Path.GetFileNameWithoutExtension(e.Name), () => stream);
							Logger?.Debug("Resource added: "+ Path.GetFileNameWithoutExtension(e.Name));
						}
						else if (e.Name.StartsWith("Server") && e.Name.EndsWith(".res"))
						{
							var stream = pkgZip.GetInputStream(e).ToMemStream();
							MemStreams.Add(stream);
							ResourceStreams.Add(Path.GetFileNameWithoutExtension(e.Name), () => stream);
							Logger?.Debug("Resource added: " + Path.GetFileNameWithoutExtension(e.Name));
						}
					}
					pkgZip.Close();
				}
			}


			// Client
            {
				var path = Path.Combine("Resources", "Client");
				var tmpDir = Path.Combine("Resources", "Temp","Client");
				Directory.CreateDirectory(path);
				if (Directory.Exists(tmpDir))
				{
					Directory.Delete(tmpDir, true);
				}
				Directory.CreateDirectory(tmpDir);
				var resourceFolders = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
				if (resourceFolders.Length!=0)
				{
					foreach (var resourceFolder in resourceFolders)
					{
						// Pack client side resource as a zip file
						Logger?.Info("Packing client-side resource: "+resourceFolder);
						var zipPath = Path.Combine(tmpDir, Path.GetFileName(resourceFolder))+".res";
						try
						{
                            using ZipFile zip = ZipFile.Create(zipPath);
                            zip.BeginUpdate();
                            foreach (var dir in Directory.GetDirectories(resourceFolder, "*", SearchOption.AllDirectories))
                            {
                                zip.AddDirectory(dir[(resourceFolder.Length + 1)..]);
                            }
                            foreach (var file in Directory.GetFiles(resourceFolder, "*", SearchOption.AllDirectories))
                            {
                                if (Path.GetFileName(file).CanBeIgnored()) { continue; }
                                zip.Add(file, file[(resourceFolder.Length + 1)..]);
                            }
                            zip.CommitUpdate();
                            zip.Close();
                            ClientResources.Add(Path.GetFileNameWithoutExtension(zipPath), () => File.OpenRead(zipPath));
                        }
						catch (Exception ex)
						{
							Logger?.Error($"Failed to pack client resource:{resourceFolder}");
							Logger?.Error(ex);
						}
					}
				}
				var packed = Directory.GetFiles(path, "*.res", SearchOption.TopDirectoryOnly);
				if (packed.Length>0)
				{
					foreach(var file in packed)
                    {
						ClientResources.Add(Path.GetFileNameWithoutExtension(file),()=>File.OpenRead(file));
                    }
				}
			}

			// Server
            {
				var path = Path.Combine("Resources", "Server");
				var dataFolder = Path.Combine(path, "data");
				Directory.CreateDirectory(path);
				foreach (var resource in Directory.GetDirectories(path))
				{
                    try
                    {
						var name = Path.GetFileName(resource);
						if (LoadedResources.ContainsKey(name))
                        {
							Logger?.Warning($"Resource \"{name}\" has already been loaded, ignoring...");
							continue;
						}
						if (name.ToLower()=="data") { continue; }
						Logger?.Info($"Loading resource: {name}");
						var r = ServerResource.LoadFrom(resource, dataFolder, Logger);
						LoadedResources.Add(r.Name, r);
					}
                    catch(Exception ex)
                    {
						Logger?.Error($"Failed to load resource: {Path.GetFileName(resource)}");
						Logger?.Error(ex);
					}
				}
				foreach (var res in Directory.GetFiles(path, "*.res", SearchOption.TopDirectoryOnly))
				{
					if (!ResourceStreams.TryAdd(Path.GetFileNameWithoutExtension(res),()=>File.OpenRead(res)))
					{
						Logger?.Warning($"Resource \"{res}\" cannot be loaded, ignoring...");
						continue;
					}
				}
				foreach(var res in ResourceStreams)
                {
					try
                    {
						var name = res.Key;
						if (LoadedResources.ContainsKey(name))
						{
							Logger?.Warning($"Resource \"{name}\" has already been loaded, ignoring...");
							continue;
						}
						Logger?.Info($"Loading resource: "+name);
						var r = ServerResource.LoadFrom(res.Value(),name, Path.Combine("Resources", "Temp", "Server"), dataFolder, Logger);
						LoadedResources.Add(r.Name, r);
					}
					catch(Exception ex)
                    {
						Logger?.Error($"Failed to load resource: {res.Key}");
						Logger?.Error(ex);
                    }
                }

				// Start scripts
				lock (LoadedResources)
				{
					foreach (var r in LoadedResources.Values)
					{
						foreach (ServerScript s in r.Scripts.Values)
						{
							s.API=Server.API;
							try
							{
								Logger?.Debug("Starting script:"+s.CurrentFile.Name);
								s.OnStart();
							}
							catch (Exception ex) { Logger?.Error($"Failed to start resource: {r.Name}"); Logger?.Error(ex); }
						}
					}
				}
				IsLoaded=true;
			}
		}

        public void UnloadAll()
		{
			lock (LoadedResources)
			{
				foreach (var d in LoadedResources.Values)
				{
					foreach (var s in d.Scripts.Values)
					{
                        try
                        {
							s.OnStop();
						}
						catch(Exception ex)
                        {
							Logger?.Error(ex);
                        }
                    }
                    try
                    {
						d.Dispose();
					}
					catch(Exception ex)
                    {
						Logger.Error($"Resource \"{d.Name}\" cannot be unloaded.");
						Logger.Error(ex);
                    }
				}
				LoadedResources.Clear();
			}
			foreach(var s in MemStreams)
            {
                try
                {
					s.Close();
					s.Dispose();
                }
				catch(Exception ex)
                {
					Logger?.Error("[Resources.CloseMemStream]",ex);
                }
            }
		}
		public void SendTo(Client client)
		{
			Task.Run(() =>
			{

				if (ClientResources.Count!=0)
				{
					Logger?.Info($"Sending resources to client:{client.Username}");
					foreach (var rs in ClientResources)
					{
						Logger?.Debug(rs.Key);
						Server.SendFile(rs.Value(),rs.Key+".res", client);
					}

					Logger?.Info($"Resources sent to:{client.Username}");
				}
				if (Server.GetResponse<Packets.FileTransferResponse>(client, new Packets.AllResourcesSent())?.Response==FileResponse.Loaded)
				{
					client.IsReady=true;
					Server.API.Events.InvokePlayerReady(client);
				}
                else
                {
					Logger?.Warning($"Client {client.Username} failed to load resource.");
                }
			});
		}
	}
}
