using ScriptableObjects;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
	public class ControlAction : MonoBehaviour
	{
		public Image throwImage;
		public Sprite[] throwSprites;

		public Image pullImage;

		private void Start()
		{
			UIManager.IsThrow = false;

			pullImage.enabled = false;
		}

		#region Buttons

		/// <summary>
		/// Perform the resist action
		/// </summary>
		public void Resist()
		{
			if(PlayerManager.LocalPlayerScript.PlayerTypeSettings.CanResist == false) return;

			PlayerManager.LocalPlayerScript.PlayerNetworkActions.CmdResist();

			_ = SoundManager.Play(CommonSounds.Instance.Click01);
			Logger.Log("Resist Button", Category.UserInput);
		}

		/// <summary>
		/// Perform the drop action
		/// </summary>
		public void Drop()
		{
			if (Validations.CanInteract(PlayerManager.LocalPlayerScript,
				    NetworkSide.Client, allowCuffed: true, apt: Validations.CheckState(x => x.CanDropItems)) == false) return;

			if (PlayerManager.LocalPlayerScript.DynamicItemStorage == null)
			{
				Logger.LogError("Tried to drop, but has no DynamicItemStorage");
				return;
			}

			var currentSlot = PlayerManager.LocalPlayerScript.DynamicItemStorage.GetActiveHandSlot();

			if (currentSlot.Item == null) return;

			if (UIManager.IsThrow)
			{
				Throw();
			}

			PlayerManager.LocalPlayerScript.PlayerNetworkActions.CmdDropItem(currentSlot.ItemStorage.gameObject.NetId(),
				currentSlot.NamedSlot.GetValueOrDefault(NamedSlot.none));
			_ = SoundManager.Play(CommonSounds.Instance.Click01);
			Logger.Log("Drop Button", Category.UserInput);
		}

		/// <summary>
		/// Throw mode toggle. Actual throw is in <see cref="MouseInputController.CheckThrow()"/>
		/// </summary>
		public void Throw(bool forceDisable = false)
		{
			if (forceDisable)
			{
				Logger.Log("Throw force disabled", Category.UserInput);
				UIManager.IsThrow = false;
				throwImage.sprite = throwSprites[0];
				return;
			}

			// See if requesting to enable or disable throw
			if (throwImage.sprite == throwSprites[0] && UIManager.IsThrow == false)
			{
				// Check if player can throw
				if (Validations.CanInteract(PlayerManager.LocalPlayerScript, NetworkSide.Client, apt:
					    Validations.CheckState(x => x.CanThrowItems)) == false) return;

				// Enable throw
				Logger.Log("Throw Button Enabled", Category.UserInput);
				_ = SoundManager.Play(CommonSounds.Instance.Click01);
				UIManager.IsThrow = true;
				throwImage.sprite = throwSprites[1];
			}
			else if (throwImage.sprite == throwSprites[1] && UIManager.IsThrow == true)
			{
				// Disable throw
				Logger.Log("Throw Button Disabled", Category.UserInput);
				UIManager.IsThrow = false;
				throwImage.sprite = throwSprites[0];
			}
		}

		/// <summary>
		/// Stops pulling whatever we're pulling
		/// </summary>
		public void StopPulling()
		{
			if (pullImage && pullImage.enabled)
			{
				var ps = PlayerManager.LocalPlayerScript.GetComponent<UniversalObjectPhysics>();
				if (ps != null)
				{
					ps.StopPulling(true);
				}
			}
		}

		#endregion

		/// <summary>
		/// Updates whether or not the "Stop Pulling" button is shown
		/// </summary>
		/// <param name="show">Whether or not to show the button</param>
		public void UpdatePullingUI(bool show)
		{
			pullImage.enabled = show;
		}
	}
}
