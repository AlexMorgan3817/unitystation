using UnityEngine;
using UnityEngine.UI;

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

	/*
	 * Button OnClick methods
	 */

	/// <summary>
	/// Perform the resist action
	/// </summary>
	public void Resist()
	{
		// TODO implement resist functionality once handcuffs and things are in
		SoundManager.Play("Click01");
		Logger.Log("Resist Button", Category.UI);
	}

	/// <summary>
	/// Perform the drop action
	/// </summary>
	public void Drop()
	{

		// if (!Validations.CanInteract(PlayerManager.LocalPlayerScript, NetworkSide.Client, allowCuffed: true)); Commented out because it does... nothing?
		UI_ItemSlot currentSlot = UIManager.Hands.CurrentSlot;
		if (currentSlot.Item == null)
		{
			return;
		}

		if(UIManager.IsThrow)
		{
			Throw();
		}
		PlayerManager.LocalPlayerScript.playerNetworkActions.CmdDropItem(currentSlot.NamedSlot);
		SoundManager.Play("Click01");
		Logger.Log("Drop Button", Category.UI);
	}

	/// <summary>
	/// Throw mode toggle. Actual throw is in <see cref="MouseInputController.CheckThrow()"/>
	/// </summary>
	public void Throw(bool forceDisable = false)
	{
		if (forceDisable)
		{
			Logger.Log("Throw force disabled", Category.UI);
			UIManager.IsThrow = false;
			throwImage.sprite = throwSprites[0];
			return;
		}

		// See if requesting to enable or disable throw
		if (throwImage.sprite == throwSprites[0] && UIManager.IsThrow == false)
		{
			// Check if player can throw
			if (!Validations.CanInteract(PlayerManager.LocalPlayerScript, NetworkSide.Client))
			{
				return;
			}

			// Enable throw
			Logger.Log("Throw Button Enabled", Category.UI);
			SoundManager.Play("Click01");
			UIManager.IsThrow = true;
			throwImage.sprite = throwSprites[1];
		}
		else if (throwImage.sprite == throwSprites[1] && UIManager.IsThrow == true)
		{
			// Disable throw
			Logger.Log("Throw Button Disabled", Category.UI);
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
			PlayerScript ps = PlayerManager.LocalPlayerScript;

			ps.pushPull.CmdStopPulling();
		}
	}

	/// <summary>
	/// Updates whether or not the "Stop Pulling" button is shown
	/// </summary>
	/// <param name="show">Whether or not to show the button</param>
	public void UpdatePullingUI(bool show)
	{
		pullImage.enabled = show;
	}
}