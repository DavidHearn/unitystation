﻿using System;
using UnityEngine;
using Mirror;
using System.Collections;
using AddressableReferences;
using Messages.Server;

namespace Items.Bureaucracy
{
	public class Photocopier : NetworkBehaviour, ICheckedInteractable<HandApply>, IServerSpawn, IRightClickable
	{
		public NetTabType NetTabType;
		public int trayCapacity;

		private Internal.Printer printer;
		private Internal.Scanner scanner;

		private PhotocopierState photocopierState = PhotocopierState.Idle;

		[SerializeField] private GameObject paperPrefab = null;

		[SerializeField] private SpriteHandler spriteHandler = null;
		private RegisterObject registerObject;

		[SerializeField] private AddressableAudioSource Copier = null;
		[SerializeField] private ItemStorage inkStorage;
		[SerializeField] private ItemTrait tonerTrait;
		public Toner InkCartadge => inkStorage.GetTopOccupiedIndexedSlot()?.ItemObject.GetComponent<Toner>();


		private void Awake()
		{
			photocopierState = PhotocopierState.Idle;
			registerObject = gameObject.GetComponent<RegisterObject>();
			printer = new Internal.Printer(0, trayCapacity, false);
			scanner = new Internal.Scanner(false, true, null, null);
			if (inkStorage == null) inkStorage = GetComponent<ItemStorage>();
		}

		public enum PhotocopierState
		{
			Idle,
			Production
		}

		#region Sprite Sync

		private void SyncPhotocopierState(PhotocopierState newState)
		{
			photocopierState = newState;
			switch (newState)
			{
				case PhotocopierState.Idle:
					spriteHandler.ChangeSprite(0);
					break;

				case PhotocopierState.Production:
					spriteHandler.ChangeSprite(1);
					break;
			}
		}

		public override void OnStartClient()
		{
			SyncPhotocopierState( photocopierState);
			base.OnStartClient();
		}

		public void OnSpawnServer(SpawnInfo info)
		{
			SyncPhotocopierState( photocopierState);
		}

		#endregion Sprite Sync

		#region Interactions

		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (!DefaultWillInteract.Default(interaction, side))
			{
				return false;
			}

			if (interaction.UsedObject != null && interaction.UsedObject.Item().HasTrait(tonerTrait)) return true;

			if (photocopierState == PhotocopierState.Idle)
			{
				if (interaction.HandObject == null) return true;
				if (printer.CanAddPageToTray(interaction.HandObject)) return true;
				if (scanner.CanPlaceDocument(interaction.HandObject)) return true;
			}

			return false;
		}

		public void ServerPerformInteraction(HandApply interaction)
		{
			if (InkCartadge == null && interaction.UsedObject != null && interaction.UsedObject.Item().HasTrait(tonerTrait))
			{
				Inventory.ServerTransfer(interaction.UsedObject.GetComponent<Pickupable>().ItemSlot,
					inkStorage.GetNextFreeIndexedSlot());
				return;
			}
			if (interaction.HandObject == null)
			{
				if (printer.TrayOpen)
				{
					Chat.AddExamineMsgFromServer(interaction.Performer, "You close the tray.");
					ToggleTray();
				}
				else if (scanner.ScannerOpen)
				{
					Chat.AddExamineMsgFromServer(interaction.Performer, "You close the scanner lid.");
					ToggleScannerLid();
				}
				else
				{
					OnGuiRenderRequired();
					TabUpdateMessage.Send(interaction.Performer, gameObject, NetTabType, TabAction.Open);
				}
			}
			else if (printer.CanAddPageToTray(interaction.HandObject))
			{
				printer = printer.AddPageToTray(interaction.HandObject);
				Chat.AddExamineMsgFromServer(interaction.Performer, "You place the sheet in the tray.");
			}
			else if (scanner.CanPlaceDocument(interaction.HandObject))
			{
				scanner = scanner.PlaceDocument(interaction.HandObject);
				Chat.AddExamineMsgFromServer(interaction.Performer, "You place the document in the scanner.");
			}
		}

		private void RemoveInkCartridge()
		{
			inkStorage.ServerDropAll();
		}

		#endregion Interactions

		#region Component API

		/*
		 * The following methods and properties are the API for this component.
		 * Other components (notably the GUI_Photocopier tab) can call these methods.
		 */

		public int TrayCount => printer.TrayCount;
		public int TrayCapacity => printer.TrayCapacity;
		public bool TrayOpen => printer.TrayOpen;
		public bool ScannerOpen => scanner.ScannerOpen;
		public bool ScannedTextNull => scanner.ScannedText == null;

		[Server]
		public void ToggleTray()
		{
			printer = printer.ToggleTray();
			OnGuiRenderRequired();
		}

		[Server]
		public void ToggleScannerLid()
		{
			scanner = scanner.ToggleScannerLid(gameObject, paperPrefab);
			OnGuiRenderRequired();
		}

		public bool CanPrint() => printer.CanPrint(scanner.ScannedText, photocopierState == PhotocopierState.Idle) && InkCartadge.CheckInkLevel();

		[Server]
		public void Print()
		{
			SyncPhotocopierState( PhotocopierState.Production);
			SoundManager.PlayNetworkedAtPos(Copier, registerObject.WorldPosition);
			StartCoroutine(WaitForPrint());
		}

		private IEnumerator WaitForPrint()
		{
			yield return WaitFor.Seconds(4f);
			SyncPhotocopierState( PhotocopierState.Idle);
			printer = printer.Print(scanner.ScannedText, gameObject, photocopierState == PhotocopierState.Idle, paperPrefab);
			OnGuiRenderRequired();
		}

		public bool CanScan() => scanner.CanScan();

		[Server]
		public void Scan()
		{
			SyncPhotocopierState( PhotocopierState.Production);
			InkCartadge.SpendInk();
			StartCoroutine(WaitForScan());
		}

		private IEnumerator WaitForScan()
		{
			yield return WaitFor.Seconds(4f);
			SyncPhotocopierState( PhotocopierState.Idle);
			scanner = scanner.Scan();
			OnGuiRenderRequired();
		}

		[Server]
		public void ClearScannedText()
		{
			scanner = scanner.ClearScannedText();
			OnGuiRenderRequired();
		}

		#endregion Component API

		/// <summary>
		/// GUI_Photocopier subscribes to this event when it is initialized.
		/// The event is triggered when Photocopier decides a GUI render is required. (something changed)
		/// </summary>
		public event EventHandler GuiRenderRequired;

		private void OnGuiRenderRequired() => GuiRenderRequired?.Invoke(gameObject, new EventArgs());

		public RightClickableResult GenerateRightClickOptions()
		{
			var result = new RightClickableResult();
			if (InkCartadge == null) return result;
			result.AddElement("Remove Ink Cart", RemoveInkCartridge);
			return result;
		}
	}
}
