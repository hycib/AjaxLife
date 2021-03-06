#region License
/* Copyright (c) 2008, Katharine Berry
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of Katharine Berry nor the names of any contributors
 *       may be used to endorse or promote products derived from this software
 *       without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY KATHARINE BERRY ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL KATHARINE BERRY BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 ******************************************************************************/
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using MiniHttpd;
using libsecondlife;
using libsecondlife.Packets;
using Newtonsoft.Json;
using Affirma.ThreeSharp;
using Affirma.ThreeSharp.Query;

namespace AjaxLife.Html
{
    public class SendMessage : IFile
    {
        // Fields
        private string name;
        private IDirectory parent;
        private Dictionary<Guid, User> users;
        private System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5CryptoServiceProvider.Create();

        // Anything in this list must be signed to be accepted. 
        // Should match (or be a subset of) the list in client/AjaxLife.Network.js.
        private string[] REQUIRED_SIGNATURES = {
            "AcceptFriendship",
            "DeclineFriendship",
            "OfferFriendship",
            "TerminateFriendship", 
            "SendAgentMoney",
            "EmptyTrash",
            "MoveItem",
            "MoveFolder",
            "MoveItems",
            "MoveFolders",
            "DeleteItem",
            "DeleteFolder",
            "DeleteMultiple",
            "GiveInventory",
            "UpdateItem",
            "UpdateFolder",
            "JoinGroup",
            "LeaveGroup",
            "ScriptPermissionResponse"
        };

        // Methods

        private bool VerifySignature(User user, string querystring)
        {
            // Check that we have enough characters to avoid an ArgumentOutOfRangeException.
            // If we don't have at least this many, there's certainly no hash anyway.
            if (querystring.Length < 38) return false;

            // All this does the same job as the following on the client side:
            // var tohash = (++AjaxLife.SignedCallCount).toString() + querystring + AjaxLife.Signature;
            // var hash = md5(tohash);
            
            // First we have to remove the hash from the incoming string. We may assume the has is always at the end.
            // This makes the job easy - we just chop the end off. No parsing required.
            // MD5s are 128 bits, or 32 hex characters, so we chop off "&hash=00000000000000000000000000000000", which is
            // 38 characters.
            string receivedhash = querystring.Substring(querystring.Length - 32); // Grab the last 32 characters.
            querystring = querystring.Remove(querystring.Length - 38); // Strip the hash off.
            ++user.SignedCallCount; // Increment the call count to ensure the same hash can't be used multiple times.
            string tohash = user.SignedCallCount.ToString() + querystring + user.Signature; // Build the to hash string.
            string expectedhash = BitConverter.ToString(md5.ComputeHash(UTF8Encoding.UTF8.GetBytes(tohash))).Replace("-", "").ToLower(); // Actually hash it.

            AjaxLife.Debug("SendMessage", "VerifySignature: Received hash " + receivedhash + ", expected " + expectedhash + " (based on '" + tohash + "')");
            // Check if they're equal.
            return (receivedhash == expectedhash);

        }

        public SendMessage(string name, IDirectory parent, Dictionary<Guid, User> users)
        {
            this.name = name;
            this.parent = parent;
            this.users = users;
        }

        public void Dispose()
        {
        }

        public void OnFileRequested(HttpRequest request, IDirectory directory)
        {
            //request.Response.SetHeader("Content-Type", "text/plain; charset=utf-8");
            request.Response.ResponseContent = new MemoryStream();
            StreamWriter textwriter = new StreamWriter(request.Response.ResponseContent);
            SecondLife client;
            AvatarTracker avatars;
            Events events;
            StreamReader reader = new StreamReader(request.PostData);
            string qstring = reader.ReadToEnd();
            reader.Dispose();
            Dictionary<string,string> POST = AjaxLife.PostDecode(qstring);
            // Pull out the session.
            if (!POST.ContainsKey("sid"))
            {
                textwriter.WriteLine("Need an SID.");
                textwriter.Flush();
                return;
            }
            Guid guid = new Guid(POST["sid"]);
            User user = new User();
            lock (this.users)
            {
                if (!this.users.ContainsKey(guid))
                {
                    textwriter.WriteLine("Error: invalid SID");
                    textwriter.Flush();
                    return;
                }
                user = this.users[guid];
                client = user.Client;
                avatars = user.Avatars;
                events = user.Events;
                user.LastRequest = DateTime.Now;
            }
            // Get the message type.
            string messagetype = POST["MessageType"];

            // Check that the message is signed if it should be.
            if (Array.IndexOf(REQUIRED_SIGNATURES, messagetype) > -1)
            {
                if (!VerifySignature(user, qstring))
                {
                    textwriter.WriteLine("Error: Received hash and expected hash do not match.");
                    textwriter.Flush();
                    return;
                }
            }
            
            // Right. This file is fun. It takes information in POST paramaters and sends them to 
            // the server in the appropriate format. Some will return data immediately, some will return
            // keys to data that will arrive in the message queue, some return nothing but you get
            // something in the message queue later, and some return nother ever.
            // 
            // The joys of dealing with multiple bizarre message types.

            switch (messagetype)
            {
                case "SpatialChat":
                    client.Self.Chat(POST["Message"], int.Parse(POST["Channel"]), (ChatType)((byte)int.Parse(POST["Type"])));
                    break;
                case "SimpleInstantMessage":
                    if (POST.ContainsKey("IMSessionID"))
                    {
                        client.Self.InstantMessage(new LLUUID(POST["Target"]), POST["Message"], new LLUUID(POST["IMSessionID"]));
                    }
                    else
                    {
                        client.Self.InstantMessage(new LLUUID(POST["Target"]), POST["Message"]);
                    }
                    break;
                case "GenericInstantMessage":
                    client.Self.InstantMessage(
                        client.Self.FirstName + " " + client.Self.LastName,
                        new LLUUID(POST["Target"]),
                        POST["Message"],
                        new LLUUID(POST["IMSessionID"]),
                        (InstantMessageDialog)((byte)int.Parse(POST["Dialog"])),
                        (InstantMessageOnline)int.Parse(POST["Online"]),
                        client.Self.SimPosition,
                        client.Network.CurrentSim.ID,
                        new byte[0]);
                    break;
                case "NameLookup":
                    client.Avatars.RequestAvatarName(new LLUUID(POST["ID"]));
                    break;
                case "Teleport":
                    {
                        Hashtable hash = new Hashtable();
                        bool status;
                        if (POST.ContainsKey("Landmark"))
                        {
                            status = client.Self.Teleport(new LLUUID(POST["Landmark"]));
                        }
                        else
                        {
                            status = client.Self.Teleport(POST["Sim"], new LLVector3(float.Parse(POST["X"]), float.Parse(POST["Y"]), float.Parse(POST["Z"])));
                        }
                        if (status)
                        {
                            hash.Add("Success", true);
                            hash.Add("Sim", client.Network.CurrentSim.Name);
                            hash.Add("Position", client.Self.SimPosition);
                        }
                        else
                        {
                            hash.Add("Success", false);
                            hash.Add("Reason", client.Self.TeleportMessage);
                        }
                        textwriter.WriteLine(MakeJson.FromHashtable(hash));
                    }
                    break;
                case "GoHome":
                    client.Self.GoHome();
                    break;
                case "GetPosition":
                    {
                        Hashtable hash = new Hashtable();
                        hash.Add("Sim", client.Network.CurrentSim.Name);
                        hash.Add("Position", client.Self.SimPosition);
                        textwriter.WriteLine(JavaScriptConvert.SerializeObject(hash));
                    }
                    break;
                case "RequestBalance":
                    client.Self.RequestBalance();
                    break;
                case "GetStats":
                    {
                        Hashtable hash = new Hashtable();
                        hash.Add("FPS", client.Network.CurrentSim.Stats.FPS);
                        hash.Add("TimeDilation", client.Network.CurrentSim.Stats.Dilation);
                        hash.Add("LSLIPS", client.Network.CurrentSim.Stats.LSLIPS);
                        hash.Add("Objects", client.Network.CurrentSim.Stats.Objects);
                        hash.Add("ActiveScripts", client.Network.CurrentSim.Stats.ActiveScripts);
                        hash.Add("Agents", client.Network.CurrentSim.Stats.Agents);
                        hash.Add("ChildAgents", client.Network.CurrentSim.Stats.ChildAgents);
                        hash.Add("AjaxLifeSessions", users.Count);
                        hash.Add("TextureCacheCount", AjaxLife.TextureCacheCount);
                        hash.Add("TextureCacheSize", AjaxLife.TextureCacheSize);
                        textwriter.WriteLine(MakeJson.FromHashtable(hash));
                    }
                    break;
                case "TeleportLureRespond":
                    client.Self.TeleportLureRespond(new LLUUID(POST["RequesterID"]), bool.Parse(POST["Accept"]));
                    break;
                case "GodlikeTeleportLureRespond":
                    {
                        LLUUID lurer = new LLUUID(POST["RequesterID"]);
                        LLUUID session = new LLUUID(POST["SessionID"]);
                        client.Self.InstantMessage(client.Self.Name, lurer, "", LLUUID.Random(), InstantMessageDialog.AcceptTeleport, InstantMessageOnline.Offline, client.Self.SimPosition, LLUUID.Zero, new byte[0]);
                        TeleportLureRequestPacket lure = new TeleportLureRequestPacket();
                        lure.Info.AgentID = client.Self.AgentID;
                        lure.Info.SessionID = client.Self.SessionID;
                        lure.Info.LureID = session;
                        lure.Info.TeleportFlags = (uint)AgentManager.TeleportFlags.ViaGodlikeLure;
                        client.Network.SendPacket(lure);
                    }
                    break;
                case "FindPeople":
                    {
                        Hashtable hash = new Hashtable();
                        hash.Add("QueryID", client.Directory.StartPeopleSearch(DirectoryManager.DirFindFlags.People, POST["Search"], int.Parse(POST["Start"])));
                        textwriter.WriteLine(MakeJson.FromHashtable(hash));
                    }
                    break;
                case "FindGroups":
                    {
                        Hashtable hash = new Hashtable();
                        hash.Add("QueryID", client.Directory.StartGroupSearch(DirectoryManager.DirFindFlags.Groups, POST["Search"], int.Parse(POST["Start"])));
                        textwriter.WriteLine(MakeJson.FromHashtable(hash));
                    }
                    break;
                case "GetAgentData":
                    client.Avatars.RequestAvatarProperties(new LLUUID(POST["AgentID"]));
                    break;
                case "StartAnimation":
                    client.Self.AnimationStart(new LLUUID(POST["Animation"]), false);
                    break;
                case "StopAnimation":
                    client.Self.AnimationStop(new LLUUID(POST["Animation"]), true);
                    break;
                case "SendAppearance":
                    client.Appearance.SetPreviousAppearance(false);
                    break;
                case "GetMapItems":
                    {
                        MapItemRequestPacket req = new MapItemRequestPacket();
                        req.AgentData.AgentID = client.Self.AgentID;
                        req.AgentData.SessionID = client.Self.SessionID;
                        GridRegion region;
                        client.Grid.GetGridRegion(POST["Region"], GridLayerType.Objects, out region);
                        req.RequestData.RegionHandle = region.RegionHandle;
                        req.RequestData.ItemType = uint.Parse(POST["ItemType"]);
                        client.Network.SendPacket((Packet)req);
                    }
                    break;
                case "GetMapBlocks":
                    {
                        MapBlockRequestPacket req = new MapBlockRequestPacket();
                        req.AgentData.AgentID = client.Self.AgentID;
                        req.AgentData.SessionID = client.Self.SessionID;
                        req.PositionData.MinX = 0;
                        req.PositionData.MinY = 0;
                        req.PositionData.MaxX = ushort.MaxValue;
                        req.PositionData.MaxY = ushort.MaxValue;
                        client.Network.SendPacket((Packet)req);
                    }
                    break;
                case "GetMapBlock":
                    {
                        ushort x = ushort.Parse(POST["X"]);
                        ushort y = ushort.Parse(POST["Y"]);
                        MapBlockRequestPacket req = new MapBlockRequestPacket();
                        req.AgentData.AgentID = client.Self.AgentID;
                        req.AgentData.SessionID = client.Self.SessionID;
                        req.PositionData.MinX = x;
                        req.PositionData.MinY = y;
                        req.PositionData.MaxX = x;
                        req.PositionData.MaxY = y;
                        client.Network.SendPacket((Packet)req);
                    }
                    break;
                case "GetOfflineMessages":
                    {
                        RetrieveInstantMessagesPacket req = new RetrieveInstantMessagesPacket();
                        req.AgentData.AgentID = client.Self.AgentID;
                        req.AgentData.SessionID = client.Self.SessionID;
                        client.Network.SendPacket((Packet)req);
                    }
                    break;
                case "GetFriendList":
                    {
                        InternalDictionary<LLUUID, FriendInfo> friends = client.Friends.FriendList;
                        List<Hashtable> friendlist = new List<Hashtable>();
                        friends.ForEach(delegate(FriendInfo friend)
                        {
                            Hashtable friendhash = new Hashtable();
                            friendhash.Add("ID", friend.UUID.ToString());
                            friendhash.Add("Name", friend.Name);
                            friendhash.Add("Online", friend.IsOnline);
                            friendhash.Add("MyRights", friend.MyFriendRights);
                            friendhash.Add("TheirRights", friend.TheirFriendRights);
                            friendlist.Add(friendhash);
                        });
                        textwriter.Write(MakeJson.FromObject(friendlist));
                    }
                    break;
                case "ChangeRights":
                    {
                        LLUUID uuid = new LLUUID(POST["Friend"]);
                        client.Friends.GrantRights(uuid, (FriendRights)int.Parse(POST["Rights"]));
                    }
                    break;
                case "RequestLocation":
                    client.Friends.MapFriend(new LLUUID(POST["Friend"]));
                    break;
                case "RequestTexture":
                    {
                        // This one's confusing, so it gets some comments.
                        // First, we get the image's UUID.
                        LLUUID image = new LLUUID(POST["ID"]);
                        // We prepare a query to ask if S3 has it. HEAD only to avoid wasting
                        // GET requests and bandwidth.
                        bool exists = false;
                        // If we already know we have it, note this.
                        if (AjaxLife.CachedTextures.Contains(image))
                        {
                            exists = true;
                        }
                        else
                        {
                            // If we're using S3, check the S3 bucket
                            if (AjaxLife.USE_S3)
                            {
                                // Otherwise, make that HEAD request and find out.
                                try
                                {
                                    IThreeSharp query = new ThreeSharpQuery(AjaxLife.S3Config);
                                    Affirma.ThreeSharp.Model.ObjectGetRequest s3request = new Affirma.ThreeSharp.Model.ObjectGetRequest(AjaxLife.TEXTURE_BUCKET, image.ToString() + ".png");
                                    s3request.Method = "HEAD";
                                    Affirma.ThreeSharp.Model.ObjectGetResponse s3response = query.ObjectGet(s3request);
                                    if (s3response.StatusCode == System.Net.HttpStatusCode.OK)
                                    {
                                        exists = true;
                                    }
                                    s3response.DataStream.Close();
                                }
                                catch { }
                            }
                            // If we aren't using S3, just check the texture cache.
                            else
                            {
                                exists = File.Exists(AjaxLife.TEXTURE_CACHE + image.ToString() + ".png");
                            }
                        }
                        // If it exists, reply with Ready = true and the URL to find it at.
                        if (exists)
                        {
                            textwriter.Write("{Ready: true, URL: \"" + AjaxLife.TEXTURE_ROOT + image + ".png\"}");
                        }
                        // If it doesn't, request the image from SL and note its lack of readiness.
                        // Notification will arrive later in the message queue.
                        else
                        {

                            client.Assets.RequestImage(image, ImageType.Normal, 125000.0f, 0);
                            textwriter.Write("{Ready: false}");
                        }
                    }
                    break;
                case "AcceptFriendship":
                    client.Friends.AcceptFriendship(client.Self.AgentID, POST["IMSessionID"]);
                    break;
                case "DeclineFriendship":
                    client.Friends.DeclineFriendship(client.Self.AgentID, POST["IMSessionID"]);
                    break;
                case "OfferFriendship":
                    client.Friends.OfferFriendship(new LLUUID(POST["Target"]));
                    break;
                case "TerminateFriendship":
                    client.Friends.TerminateFriendship(new LLUUID(POST["Target"]));
                    break;
                case "SendAgentMoney":
                    client.Self.GiveAvatarMoney(new LLUUID(POST["Target"]), int.Parse(POST["Amount"]));
                    break;
                case "RequestAvatarList":
                    {
                        List<Hashtable> list = new List<Hashtable>();
                        foreach (KeyValuePair<uint, Avatar> pair in avatars.Avatars)
                        {
                            Avatar avatar = pair.Value;
                            Hashtable hash = new Hashtable();
                            hash.Add("Name", avatar.Name);
                            hash.Add("ID", avatar.ID);
                            hash.Add("LocalID", avatar.LocalID);
                            hash.Add("Position", avatar.Position);
                            //hash.Add("Rotation", avatar.Rotation);
                            hash.Add("Scale", avatar.Scale);
                            hash.Add("GroupName", avatar.GroupName);
                            list.Add(hash);
                        }
                        textwriter.Write(MakeJson.FromObject(list));
                    }
                    break;
                case "LoadInventoryFolder":
                    client.Inventory.RequestFolderContents(new LLUUID(POST["UUID"]), client.Self.AgentID, true, true, InventorySortOrder.ByDate | InventorySortOrder.SystemFoldersToTop);
                    break;
                case "RequestAsset":
                    {
                        try
                        {
                            LLUUID transferid = client.Assets.RequestInventoryAsset(new LLUUID(POST["AssetID"]), new LLUUID(POST["InventoryID"]),
                                LLUUID.Zero, new LLUUID(POST["OwnerID"]), (AssetType)int.Parse(POST["AssetType"]), false);
                            textwriter.Write("{TransferID: \"" + transferid + "\"}");
                        }
                        catch // Try catching the error that sometimes gets thrown... but sometimes doesn't.
                        {

                        }
                    }
                    break;
                case "SendTeleportLure":
                    client.Self.SendTeleportLure(new LLUUID(POST["Target"]), POST["Message"]);
                    break;
                case "ScriptPermissionResponse":
                    client.Self.ScriptQuestionReply(client.Network.CurrentSim, new LLUUID(POST["ItemID"]), new LLUUID(POST["TaskID"]), (ScriptPermission)int.Parse(POST["Permissions"]));
                    break;
                case "ScriptDialogReply":
                    {
                        ScriptDialogReplyPacket packet = new ScriptDialogReplyPacket();
                        packet.AgentData.AgentID = client.Self.AgentID;
                        packet.AgentData.SessionID = client.Self.SessionID;
                        packet.Data.ButtonIndex = int.Parse(POST["ButtonIndex"]);
                        packet.Data.ButtonLabel = Helpers.StringToField(POST["ButtonLabel"]);
                        packet.Data.ChatChannel = int.Parse(POST["ChatChannel"]);
                        packet.Data.ObjectID = new LLUUID(POST["ObjectID"]);
                        client.Network.SendPacket((Packet)packet);
                    }
                    break;
                case "SaveNotecard":
                    client.Inventory.RequestUploadNotecardAsset(Helpers.StringToField(POST["AssetData"]), new LLUUID(POST["ItemID"]), new InventoryManager.NotecardUploadedAssetCallback(events.Inventory_OnNoteUploaded));
                    break;
                case "CreateInventory":
                    client.Inventory.RequestCreateItem(new LLUUID(POST["Folder"]), POST["Name"], POST["Description"], (AssetType)int.Parse(POST["AssetType"]), LLUUID.Random(), (InventoryType)int.Parse(POST["InventoryType"]), PermissionMask.All, new InventoryManager.ItemCreatedCallback(events.Inventory_OnItemCreated));
                    break;
                case "CreateFolder":
                    {
                        LLUUID folder = client.Inventory.CreateFolder(new LLUUID(POST["Parent"]), POST["Name"]);
                        textwriter.Write("{FolderID: \"" + folder + "\"}");
                    }
                    break;
                case "EmptyTrash":
                    client.Inventory.EmptyTrash();
                    break;
                case "MoveItem":
                    client.Inventory.MoveItem(new LLUUID(POST["Item"]), new LLUUID(POST["TargetFolder"]), POST["NewName"]);
                    break;
                case "MoveFolder":
                    client.Inventory.MoveFolder(new LLUUID(POST["Folder"]), new LLUUID(POST["NewParent"]));
                    break;
                case "MoveItems":
                case "MoveFolders":
                    {
                        Dictionary<LLUUID, LLUUID> dict = new Dictionary<LLUUID, LLUUID>();
                        string[] moves = POST["ToMove"].Split(',');
                        for (int i = 0; i < moves.Length; ++i)
                        {
                            string[] move = moves[i].Split(' ');
                            dict.Add(new LLUUID(move[0]), new LLUUID(move[1]));
                        }
                        if (messagetype == "MoveItems")
                        {
                            client.Inventory.MoveItems(dict);
                        }
                        else if (messagetype == "MoveFolders")
                        {
                            client.Inventory.MoveFolders(dict);
                        }
                    }
                    break;
                case "DeleteItem":
                    client.Inventory.RemoveItem(new LLUUID(POST["Item"]));
                    break;
                case "DeleteFolder":
                    client.Inventory.RemoveFolder(new LLUUID(POST["Folder"]));
                    break;
                case "DeleteMultiple":
                    {
                        string[] items = POST["Items"].Split(',');
                        List<LLUUID> itemlist = new List<LLUUID>();
                        for (int i = 0; i < items.Length; ++i)
                        {
                            itemlist.Add(new LLUUID(items[i]));
                        }
                        string[] folders = POST["Folders"].Split(',');
                        List<LLUUID> folderlist = new List<LLUUID>();
                        for (int i = 0; i < items.Length; ++i)
                        {
                            folderlist.Add(new LLUUID(folders[i]));
                        }
                        client.Inventory.Remove(itemlist, folderlist);
                    }
                    break;
                case "GiveInventory":
                    {
                        client.Inventory.GiveItem(new LLUUID(POST["ItemID"]), POST["ItemName"], (AssetType)int.Parse(POST["AssetType"]), new LLUUID(POST["Recipient"]), true);
                    }
                    break;
                case "UpdateItem":
                    {
                        InventoryItem item = client.Inventory.FetchItem(new LLUUID(POST["ItemID"]), new LLUUID(POST["OwnerID"]), 1000);
                        if (POST.ContainsKey("Name")) item.Name = POST["Name"];
                        if (POST.ContainsKey("Description")) item.Description = POST["Description"];
                        if (POST.ContainsKey("NextOwnerMask")) item.Permissions.NextOwnerMask = (PermissionMask)uint.Parse(POST["NextOwnerMask"]);
                        if (POST.ContainsKey("SalePrice")) item.SalePrice = int.Parse(POST["SalePrice"]);
                        if (POST.ContainsKey("SaleType")) item.SaleType = (SaleType)int.Parse(POST["SaleType"]); // This should be byte.Parse, but this upsets mono's compiler (CS1002)
                        client.Inventory.RequestUpdateItem(item);
                    }
                    break;
                case "UpdateFolder":
                    {
                        UpdateInventoryFolderPacket packet = new UpdateInventoryFolderPacket();
                        packet.AgentData.AgentID = client.Self.AgentID;
                        packet.AgentData.SessionID = client.Self.SessionID;
                        packet.FolderData = new UpdateInventoryFolderPacket.FolderDataBlock[1];
                        packet.FolderData[0] = new UpdateInventoryFolderPacket.FolderDataBlock();
                        packet.FolderData[0].FolderID = new LLUUID(POST["FolderID"]);
                        packet.FolderData[0].ParentID = new LLUUID(POST["ParentID"]);
                        packet.FolderData[0].Type = sbyte.Parse(POST["Type"]);
                        packet.FolderData[0].Name = Helpers.StringToField(POST["Name"]);
                        client.Network.SendPacket((Packet)packet);
                    }
                    break;
                case "FetchItem":
                    client.Inventory.FetchItem(new LLUUID(POST["Item"]), new LLUUID(POST["Owner"]), 5000);
                    break;
                case "ReRotate":
                    user.Rotation = -Math.PI;
                    break;
                case "StartGroupIM":
                    AjaxLife.Debug("SendMessage", "RequestJoinGroupChat(" + POST["Group"] + ")");
                    client.Self.RequestJoinGroupChat(new LLUUID(POST["Group"]));
                    break;
                case "GroupInstantMessage":
                    client.Self.InstantMessageGroup(new LLUUID(POST["Group"]), POST["Message"]);
                    break;
                case "RequestGroupProfile":
                    client.Groups.RequestGroupProfile(new LLUUID(POST["Group"]));
                    break;
                case "RequestGroupMembers":
                    client.Groups.RequestGroupMembers(new LLUUID(POST["Group"]));
                    break;
                case "RequestGroupName":
                    client.Groups.RequestGroupName(new LLUUID(POST["ID"]));
                    break;
                case "JoinGroup":
                    client.Groups.RequestJoinGroup(new LLUUID(POST["Group"]));
                    break;
                case "LeaveGroup":
                    client.Groups.LeaveGroup(new LLUUID(POST["Group"]));
                    break;
                case "RequestCurrentGroups":
                    client.Groups.RequestCurrentGroups();
                    break;
                case "GetParcelID":
                    textwriter.Write("{LocalID: "+client.Parcels.GetParcelLocalID(client.Network.CurrentSim, new LLVector3(float.Parse(POST["X"]), float.Parse(POST["Y"]), float.Parse(POST["Z"])))+"}");
                    break;
                case "RequestParcelProperties":
                    client.Parcels.PropertiesRequest(client.Network.CurrentSim, int.Parse(POST["LocalID"]), int.Parse(POST["SequenceID"]));
                    break;
            }
            textwriter.Flush();
        }

        // Properties
        public string ContentType
        {
            get
            {
                return "application/json";
            }
        }

        public string Name
        {
            get
            {
                return this.name;
            }
        }

        public IDirectory Parent
        {
            get
            {
                return this.parent;
            }
        }
    }
}