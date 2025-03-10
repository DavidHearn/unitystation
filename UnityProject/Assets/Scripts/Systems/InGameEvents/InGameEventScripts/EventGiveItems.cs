﻿using System.Collections.Generic;
using Managers;
using Strings;
using UnityEngine;

namespace InGameEvents
{
	public class EventGiveItems : EventScriptBase
	{
		[SerializeField]
		private List<GameObject> itemList = new List<GameObject>();
		[SerializeField]
		private string announceText = "Blue space anomaly near your location has flung out objects near your location.";

		public override void OnEventStart()
		{
			if (!FakeEvent)
			{
				SpawnItems();
			}

			base.OnEventStart();
		}

		private void SpawnItems()
		{
			if (itemList.Count == 0) return;

			foreach (var player in PlayerList.Instance.InGamePlayers)
			{
				if (player.Script.IsDeadOrGhost) continue;
				if (player.Script.IsNormal == false) continue;

				var slot = player.Script.Equipment.ItemStorage.GetActiveHandSlot();

				if (slot == null) continue;

				if (slot.Item == null)
				{
					var item = Spawn.ServerPrefab(itemList[UnityEngine.Random.Range(0, itemList.Count)], player.Script.WorldPos, player.Script.gameObject.transform.parent, player.Script.transform.rotation);

					Inventory.ServerAdd(item.GameObject.GetComponent<Pickupable>(), slot);
				}
				else
				{
					Spawn.ServerPrefab(itemList[UnityEngine.Random.Range(0, itemList.Count)], player.Script.WorldPos, player.Script.gameObject.transform.parent, player.Script.transform.rotation);
				}
			}
		}

		public override void OnEventEndTimed()
		{
			if (AnnounceEvent)
			{
				CentComm.MakeAnnouncement(ChatTemplates.CentcomAnnounce, announceText, CentComm.UpdateSound.Alert);
			}
		}
	}
}