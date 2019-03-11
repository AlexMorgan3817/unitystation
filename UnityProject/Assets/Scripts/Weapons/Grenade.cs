using System.Collections;
using System.Collections.Generic;
using Light2D;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

/// <summary>
///     shape of explosion that occurs
/// </summary>
public enum ExplosionType
{
	Square, // radius is equal in all directions from center []
	
	Diamond, // classic SS13 diagonals are reduced and angled <>
	Bomberman, // plus +
	Circle, // Diamond without tip
}

/// <summary>
///     Generic grenade base.
/// </summary>
public class Grenade : PickUpTrigger
{
	[TooltipAttribute("If the fuse is precise or has a degree of error equal to fuselength / 4")]
	public bool unstableFuse = false;
	[TooltipAttribute("If explosion radius has a degree of error equal to radius / 4")]
	public bool unstableRadius = false;
	[TooltipAttribute("Explosion Damage")]
	public int damage = 150;
	[TooltipAttribute("Explosion Radius in tiles")]
	public float radius = 4f;
	[TooltipAttribute("Shape of the explosion")]
	public ExplosionType explosionType;
	[TooltipAttribute("fuse timer in seconds")]
	public float fuseLength = 3;
	[TooltipAttribute("Distance multiplied from explosion that will still shake = shakeDistance * radius")]
	public float shakeDistance = 4;
	[TooltipAttribute("generally neccesary for smaller explosions = 1 - ((distance + distance) / ((radius + radius) + minDamage))")]
	public int minDamage = 2;
	[TooltipAttribute("Maximum duration grenade effects are visible depending on distance from center")]
	public float maxEffectDuration = .25f;
	[TooltipAttribute("Minimum duration grenade effects are visible depending on distance from center")]
	public float minEffectDuration = .05f;
	
	private readonly string[] EXPLOSION_SOUNDS = { "Explosion1", "Explosion2" };
	//LayerMask for things that can be damaged
	private int DAMAGEABLE_MASK;
	//LayerMask for obstructions which can block the explosion
	private int OBSTACLE_MASK;
	//collider array to re-use when checking for collisions with the explosion
	private readonly List<Collider2D> colliders = new List<Collider2D>();

	//whether this object has exploded
	private bool hasExploded;	
	//this object's registerObject
    private bool timerRunning = false;
	private RegisterObject registerObject;
	//this object's custom net transform
	private CustomNetTransform customNetTransform;

    private ObjectBehaviour objectBehaviour;
	private TileChangeManager tileChangeManager;

	private void Start()
	{
		DAMAGEABLE_MASK = LayerMask.GetMask("Players", "Machines", "Default" /*, "Lighting", "Items"*/);
		OBSTACLE_MASK = LayerMask.GetMask("Walls", "Door Closed");

		registerObject = GetComponent<RegisterObject>();
		customNetTransform = GetComponent<CustomNetTransform>();
        objectBehaviour = GetComponent<ObjectBehaviour>();
		tileChangeManager = GetComponentInParent<TileChangeManager>();
	}

	public override void UI_Interact(GameObject originator, string hand)
	{
		if (!isServer)
        { 
            InteractMessage.Send(gameObject, hand, true);
        }
		else
		{
        	StartCoroutine(TimeExplode());
		}
	}

    private IEnumerator TimeExplode()
    {
        if (!timerRunning)
        {
            timerRunning = true;
			PlayPinSFX();
			if (unstableFuse)
			{
				float fuseVariation = fuseLength / 4;
				fuseLength = Random.Range(fuseLength - fuseVariation, fuseLength + fuseVariation);
			}
			if (unstableRadius)
			{
				float radiusVariation = radius / 4;
				radius = Random.Range(radius - radiusVariation, radius + radiusVariation);
			}
            yield return new WaitForSeconds(fuseLength);
            Explode("explosion");
        }
    }
	
	public void Explode(string damagedBy)
	{
		if (hasExploded)
		{
			return;
		}
		hasExploded = true;
		if (isServer)
		{
			playExplodeSound();
			createShape();
			CalcAndApplyExplosionDamage(damagedBy);
			DisappearObject();
		}
	}

	/// <summary>
	/// Calculate and apply the damage that should be caused by the explosion, updating the server's state for the damaged
	/// objects. Currently always uses a circle
	/// </summary>
	/// <param name="thanksTo">string of the entity that caused the explosion</param>
	[Server]
	public void CalcAndApplyExplosionDamage(string thanksTo)
	{
		Vector2 explosionPos = objectBehaviour.AssumedLocation().To2Int();
		int length = colliders.Count;
		Dictionary<GameObject, int> toBeDamaged = new Dictionary<GameObject, int>();
		for (int i = 0; i < length; i++)
		{
			Collider2D localCollider = colliders[i];
			GameObject localObject = localCollider.gameObject;

			Vector2 localObjectPos = localObject.transform.position;
			float distance = Vector3.Distance(explosionPos, localObjectPos);
			float effect = 1 - ((distance + distance) / ((radius + radius) + minDamage));
			int actualDamage = (int)(damage * effect);

			if (NotSameObject(localCollider) && HasHealthComponent(localCollider))
				 //todo check why it's reaching negative values anyway)
			{
				if (IsWithinReach(explosionPos, localObjectPos, distance) && HasEffectiveDamage(actualDamage))
				{
					toBeDamaged[localObject] = actualDamage;
				}
				// Shake if the player is in reach of the explosion
				if (IsWIthinShakeReach(distance))
				{
					Camera2DFollow.followControl.Shake(distanceFromCenter(0, (int)distance, .05f, .3f), 0.2f);
				}
			}
		}

		foreach (KeyValuePair<GameObject, int> pair in toBeDamaged)
		{
			pair.Key.GetComponent<LivingHealthBehaviour>()
				.ApplyDamage(pair.Key, pair.Value, DamageType.Burn);
		}
	}

	private bool HasEffectiveDamage(int actualDamage)
	{
		return actualDamage > 0;
	}

	private bool IsWithinReach(Vector2 pos, Vector2 damageablePos, float distance)
	{
		return Physics2D.Raycast(pos, damageablePos - pos, distance, OBSTACLE_MASK).collider == null;
	}

	private bool IsPastWall(Vector2 pos, Vector2 damageablePos, float distance)
	{
		return Physics2D.Raycast(pos, damageablePos - pos, distance, OBSTACLE_MASK).collider == null;
	}


	private bool IsWIthinShakeReach(float distance)
	{
		return distance <= (radius * shakeDistance);
	}


	private static bool HasHealthComponent(Collider2D localCollider)
	{
		return localCollider.gameObject.GetComponent<LivingHealthBehaviour>() != null;
	}

	private bool NotSameObject(Collider2D localCollider)
	{
		return !localCollider.gameObject.Equals(gameObject);
	}

	/// <summary>
	/// Handles the visual effect of the explosion / disappearing of the object
	/// </summary>
	private void playExplodeSound()
	{

		// Instantiate a clone of the source so that multiple explosions can play at the same time.
		string name = EXPLOSION_SOUNDS[Random.Range(0, EXPLOSION_SOUNDS.Length)];
		AudioSource source = SoundManager.Instance[name];
        Vector3Int explodePosition = objectBehaviour.AssumedLocation().RoundToInt();
		if (source != null)
		{
			Instantiate(source, explodePosition, Quaternion.identity).Play();
		}

	}

	/// <summary>
	/// disappear this object (while still keeping the explosion around)
	/// </summary>
	private void DisappearObject()
	{
		if (isServer)
		{
			//make it vanish in the server's state of the world
			//this currently removes it from the world and any player inventory
			//backpack slots need a way of being cleared
			customNetTransform.DisappearFromWorldServer();
            InventorySlot invSlot = InventoryManager.GetSlotFromItem(gameObject);
			if (invSlot != null)
			{
				InventoryManager.DestroyItemInSlot(invSlot);
			}
		}
		else
		{
			//make it vanish in the client's local world
			customNetTransform.DisappearFromWorld();
		}		
	}

	/// <summary>
	/// Set the tiles to show fire effect in the pattern that was chosen
	/// This could be used in the future to set it as chemical reactions in a location instead.
	/// </summary>
	private void createShape()
	{
		int radiusInteger = (int)radius;
		Vector3Int pos = Vector3Int.RoundToInt(objectBehaviour.AssumedLocation());
		if (explosionType == ExplosionType.Square)
		{
			for (int i = -radiusInteger; i <= radiusInteger; i++)
			{
				for (int j = -radiusInteger; j <= radiusInteger; j++)
				{
					Vector3Int checkPos = new Vector3Int(pos.x + i, pos.y + j, 0);
					if (IsPastWall(pos.To2Int(), checkPos.To2Int(), Mathf.Abs(i) + Mathf.Abs(j)))
					{
						checkColliders(checkPos.To2Int());
						checkPos.x -= 1;
						checkPos.y -= 1;
						StartCoroutine(TimedEffect(checkPos, TileType.Effects, "Fire", distanceFromCenter(i,j, minEffectDuration, maxEffectDuration)));
					}
				}
			}
		}
		if (explosionType == ExplosionType.Diamond)
		{
			// F is distance from zero, calculated by radius - x
			// if pos.x/pos.y is within that range it will apply affect that position
			int f;
			for (int i = -radiusInteger; i <= radiusInteger; i++)
			{
				f = radiusInteger - Mathf.Abs(i);
				for (int j = -radiusInteger; j <= radiusInteger; j++)
				{
					if (j <= 0 && j >= (-f) || j >= 0 && j <= (0 + f))
					{
						Vector3Int diamondPos = new Vector3Int(pos.x + i, pos.y + j, 0);
						if (IsPastWall(pos.To2Int(), diamondPos.To2Int(), Mathf.Abs(i) + Mathf.Abs(j)))
						{
							checkColliders(diamondPos.To2Int());
							diamondPos.x -= 1;
							diamondPos.y -= 1;
							StartCoroutine(TimedEffect(diamondPos, TileType.Effects, "Fire", distanceFromCenter(i,j, minEffectDuration, maxEffectDuration)));
						}
					}
				}
			}
		}
		if (explosionType == ExplosionType.Bomberman)
		{
			for (int i = -radiusInteger; i <= radiusInteger; i++)
			{
				Vector3Int xPos = new Vector3Int(pos.x + i, pos.y, 0);
				if (IsPastWall(pos.To2Int(), xPos.To2Int(), Mathf.Abs(i)))
				{
					checkColliders(xPos.To2Int());
					xPos.x -= 1;
					xPos.y -= 1;
					StartCoroutine(TimedEffect(xPos, TileType.Effects, "Fire", distanceFromCenter(i,0, minEffectDuration, maxEffectDuration)));
				}
			}
			for (int j = -radiusInteger; j <= radiusInteger; j++)
			{
				Vector3Int yPos = new Vector3Int(pos.x, pos.y + j, 0);
				if (IsPastWall(pos.To2Int(), yPos.To2Int(), Mathf.Abs(j)))
				{
					checkColliders(yPos.To2Int());
					yPos.x -= 1;
					yPos.y -= 1;
					StartCoroutine(TimedEffect(yPos, TileType.Effects, "Fire", distanceFromCenter(0,j, minEffectDuration, maxEffectDuration)));
				}
			}
		}
		if (explosionType == ExplosionType.Circle)
		{
			// F is distance from zero, calculated by radius - x
			// if pos.x/pos.y is within that range it will apply affect that position
			int f;
			for (int i = -radiusInteger; i <= radiusInteger; i++)
			{
				f = radiusInteger - Mathf.Abs(i) + 1;
				for (int j = -radiusInteger; j <= radiusInteger; j++)
				{
					if (j <= 0 && j >= (-f) || j >= 0 && j <= (0 + f))
					{
						Vector3Int circlePos = new Vector3Int(pos.x + i, pos.y + j, 0);
						if (IsPastWall(pos.To2Int(), circlePos.To2Int(), Mathf.Abs(i) + Mathf.Abs(j)))
						{
							checkColliders(circlePos.To2Int());
							circlePos.x -= 1;
							circlePos.y -= 1;
							StartCoroutine(TimedEffect(circlePos, TileType.Effects, "Fire", distanceFromCenter(i,j, minEffectDuration, maxEffectDuration)));
						}
					}
				}
			}
		}
	}

	private void checkColliders(Vector2 position)
	{
		Collider2D victim = Physics2D.OverlapPoint(position);
		if (victim)
		{
			colliders.Add(victim);
		}
	}

	public IEnumerator TimedEffect(Vector3Int position, TileType tileType, string tileName, float time)
	{
		tileChangeManager.UpdateTile(position, TileType.Effects, "Fire");
        yield return new WaitForSeconds(time);
		tileChangeManager.RemoveTile(position, LayerType.Effects);
	}

	/// <summary>
	/// calculates the distance from the the center using the looping x and y vars
	/// returns a float between the limits
	/// </summary>
	private float distanceFromCenter(int x, int y, float lowLimit = 0.05f, float Highlimit = 0.25f)
	{
		float percentage = (Mathf.Abs(x) + Mathf.Abs(y)) / (radius + radius);
		float reversedPercentage = (1 - percentage) * 100;
		float distance = ((reversedPercentage * (Highlimit - lowLimit) / 100) + lowLimit);
		return distance;
	}

	private void PlayPinSFX()
	{
		PlayerManager.LocalPlayerScript.soundNetworkActions.CmdPlaySoundAtPlayerPos("EmptyGunClick");
	}

}