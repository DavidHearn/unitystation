﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Systems.Explosions;
using Systems.Radiation;
using Items.Engineering;
using Objects.Atmospherics;
using Objects.Engineering.Reactor;
using Shared.Systems.ObjectConnection;


namespace Objects.Engineering
{
	public class ReactorGraphiteChamber : MonoBehaviour, ICheckedInteractable<HandApply>, IMultitoolMasterable, IServerDespawn
	{
		public float EditorPresentNeutrons;
		public float EditorEnergyReleased;

		public GameObject UraniumOre;
		public GameObject MetalOre;

		public GameObject ConstructMaterial; //Was set to PlasSteel. Changed to generic material in anticipation of changing to graphite in future.

		[SerializeField] private int droppedMaterialAmount = 40;

		[SerializeField] private ItemStorage RodStorage = default;
		[SerializeField] private ItemStorage PipeStorage = default;

		private decimal NeutronLeakingChance = 0.0397M;

		public decimal EnergyReleased = 0; //Wattsec

		public ItemTrait PipeItemTrait = null;

		public float LikelihoodOfSpontaneousNeutron = 0.1f;

		public System.Random RNG = new System.Random();

		public decimal PresentNeutrons = 0;

		public RadiationProducer radiationProducer;
		private RegisterObject registerObject;
		public ReactorPipe ReactorPipe;

		public ReactorChamberRod[] ReactorRods = new ReactorChamberRod[16];
		public List<FuelRod> ReactorFuelRods = new List<FuelRod>();
		public List<EngineStarter> ReactorEngineStarters = new List<EngineStarter>();

		public float ControlRodDepthPercentage = 1;

		private float EnergyToEvaporateWaterPer1 = 2000;

		public float RodMeltingTemperatureK = 1100;
		private float BoilingPoint = 373.15f;

		public bool MeltedDown = false;
		public bool PoppedPipes = false;

		public decimal NeutronSingularity = 76488300000M;
		public decimal CurrentPressure = 0;

		public decimal MaxPressure = 120000;

		public decimal KFactor
		{
			get { return (CalculateKFactor()); }
		}

		#region Lifecycle

		private void Awake()
		{
			radiationProducer = this.GetComponent<RadiationProducer>();
			registerObject = this.GetComponent<RegisterObject>();
			ReactorPipe = this.GetComponent<ReactorPipe>();
		}

		public void OnEnable()
		{
			if (CustomNetworkManager.IsServer == false) return;
			UpdateManager.Add(CycleUpdate, 1);
		}

		public void OnDisable()
		{
			if (CustomNetworkManager.IsServer == false) return;
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, CycleUpdate);
		}


		/// <summary>
		/// is the function to denote that it will be pooled or destroyed immediately after this function is finished, Used for cleaning up anything that needs to be cleaned up before this happens
		/// </summary>
		public void OnDespawnServer(DespawnInfo info)
		{
			if (MeltedDown)
			{
				foreach (var Rod in ReactorRods)
				{
					if (Rod != null)
					{
						switch (Rod.GetRodType())
						{
							case RodType.Fuel:
								Spawn.ServerPrefab(UraniumOre, registerObject.WorldPositionServer);
								break;
							case RodType.Control:
								Spawn.ServerPrefab(MetalOre, registerObject.WorldPositionServer);
								break;
						}
					}
				}
			}
			else
			{
				foreach (var Rod in RodStorage.GetItemSlots())
				{
					Inventory.ServerDespawn(Rod);
				}

				Spawn.ServerPrefab(ConstructMaterial, registerObject.WorldPositionServer, count: droppedMaterialAmount);
			}


			MeltedDown = false;
			PoppedPipes = false;
			PresentNeutrons = 0;
			Array.Clear(ReactorRods, 0, ReactorRods.Length);
			ReactorFuelRods.Clear();
			ReactorEngineStarters.Clear();
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, CycleUpdate);
		}

		#endregion

		public decimal CalculateKFactor()
		{
			decimal K = 0.85217022M * NonNeutronAbsorptionProbability();
			return (K);
		}

		public void SetControlRodDepth(float RequestedDepth)
		{
			ControlRodDepthPercentage = Mathf.Clamp(RequestedDepth, 0.1f, 1f);
			;
		}

		public float Temperature => GetTemperature();

		public float GetTemperature()
		{
			return (ReactorPipe.pipeData.mixAndVolume.Temperature);
		}

		public decimal NonNeutronAbsorptionProbability()
		{
			decimal AbsorptionPower = 0;
			decimal NumberOfRods = 0;
			foreach (var Rod in ReactorRods)
			{
				var controlRod = Rod as ControlRod;
				if (controlRod != null)
				{
					AbsorptionPower += controlRod.AbsorptionPower;
				}
				else
				{
					NumberOfRods++;
				}
			}

			if (MeltedDown == false)
			{
				return (NumberOfRods / (ReactorRods.Length + (AbsorptionPower * (decimal) ControlRodDepthPercentage)));
			}
			else
			{
				return ((decimal) (100f / (100f + ReactorPipe.pipeData.mixAndVolume.Total.x)) *
				        (NumberOfRods / ReactorRods.Length));
			}

			//return (0.71M); // Unreachable
		}

		/*public decimal NeutronGenerationProbability()
		{
			decimal NumberOfRods = 0;
			foreach (var Rod in ReactorRods)
			{
				var fuelRod = Rod as FuelRod;
				if (fuelRod != null)
				{
					NumberOfRods++;
					fuelRod.energyPerAtom
				}
			}
			//Depends on the material Being input
			return (1.65M);
		}*/

		public void CycleUpdate()
		{
			if (GetTemperature() > RodMeltingTemperatureK && !MeltedDown)
			{
				MeltedDown = true;
			}


			int SpontaneousNeutronProbability = RNG.Next(0, 10001);
			if ((decimal) LikelihoodOfSpontaneousNeutron > (SpontaneousNeutronProbability / 1000M))
			{
				PresentNeutrons += 1;
			}

			foreach (var Starter in ReactorEngineStarters)
			{
				PresentNeutrons += (decimal) Starter.NeutronGenerationPerSecond;
			}

			PresentNeutrons += ExternalNeutronGeneration();
			GenerateExternalRadiation();
			PresentNeutrons *= KFactor;
			if (NeutronSingularity < PresentNeutrons)
			{
				Explosion.StartExplosion(registerObject.WorldPositionServer, 120000);
				PresentNeutrons = 0;
				if (this != null)
				{
					_ = Despawn.ServerSingle(gameObject);
				}
			}


			EditorPresentNeutrons = (float) PresentNeutrons;
			PowerOutput();

			if (PoppedPipes) //Its blown up so not connected so vent to steam
			{
				var WholeHeatCapacity = ReactorPipe.pipeData.mixAndVolume.GetReagentMix().WholeHeatCapacity;
				var Temperature = ReactorPipe.pipeData.mixAndVolume.Temperature;

				if (WholeHeatCapacity > 0 &&
				    Temperature > BoilingPoint)
				{
					var ExcessEnergy = (Temperature - BoilingPoint) * WholeHeatCapacity;
					var AmountBoiledOff = ExcessEnergy / (EnergyToEvaporateWaterPer1);
					_ = ReactorPipe.pipeData.mixAndVolume.GetReagentMix().Take(AmountBoiledOff);
				}
			}

			//Sprites
			//Reduce  sound of geiger counter
			//Coloring numbers in UI with red - bad, green - good.
			//2) Tooltips when hovering on buttons/slider, like foma did with action buttons.
			//1) Damage for RWalls and players from explosions
			//2)Nerf easy sabotage for Reactor or people will be blowing it too fast
			//Synchronise radiation
		}

		public void GenerateExternalRadiation()
		{
			if (PresentNeutrons > 0)
			{
				var LeakedNeutrons = PresentNeutrons * NeutronLeakingChance;
				LeakedNeutrons =
					(((LeakedNeutrons /
					   (LeakedNeutrons + ((decimal) Math.Pow((double) LeakedNeutrons, (double) 0.82M)))) -
					  0.5M) * 2 * 36000);
				radiationProducer.SetLevel((float) LeakedNeutrons);
			}
		}

		public float RadiationAboveCore()
		{
			return (registerObject.Matrix.GetMetaDataNode(registerObject.LocalPosition)
				.RadiationNode
				.CalculateRadiationLevel());
		}

		public decimal ExternalNeutronGeneration()
		{
			return ((decimal) registerObject.Matrix.GetMetaDataNode(registerObject.LocalPosition)
				.RadiationNode
				.CalculateRadiationLevel(radiationProducer.ObjectID));
		}

		public void PowerOutput()
		{
			EnergyReleased = ProcessRodsHits(PresentNeutrons);
			EditorEnergyReleased = (float) EnergyReleased;

			uint rods = 0;
			foreach (var rod in ReactorRods)
			{
				if (rod != null)
				{
					rods++;
				}
			}

			var ExtraEnergyGained = (float) EnergyReleased;
			if (float.IsNormal(ExtraEnergyGained) == false && ExtraEnergyGained != 0)
			{
				Logger.LogError(
					$"PowerOutput Graphite chamber invalid number from EnergyReleased With Float of {ExtraEnergyGained} With decimal of {EnergyReleased}");
				ExtraEnergyGained = 0;
			}

			if (ReactorPipe.pipeData.mixAndVolume.WholeHeatCapacity != 0)
			{
				ReactorPipe.pipeData.mixAndVolume.InternalEnergy =
					ReactorPipe.pipeData.mixAndVolume.InternalEnergy + ExtraEnergyGained;
			}

			CurrentPressure = (decimal) Mathf.Clamp(((ReactorPipe.pipeData.mixAndVolume.Temperature - 293.15f) *
			                                         ReactorPipe.pipeData.mixAndVolume.Total.x),
				(float) decimal.MinValue, (float) decimal.MaxValue);

			if (CurrentPressure > MaxPressure)
			{
				PoppedPipes = true;
				var EmptySlot = PipeStorage.GetIndexedItemSlot(0);
				Inventory.ServerDrop(EmptySlot);
			}
		}

		public decimal ProcessRodsHits(decimal AbsorbedNeutrons)
		{
			decimal TotalEnergy = 0;
			decimal GeneratedNeutrons = 0;
			foreach (var Rod in ReactorFuelRods)
			{
				Tuple<decimal, decimal> Output = Rod.ProcessRodHit(AbsorbedNeutrons / ReactorFuelRods.Count);
				TotalEnergy += Output.Item1;
				GeneratedNeutrons += Output.Item2;
			}

			PresentNeutrons = GeneratedNeutrons;

			return (TotalEnergy);
		}
		//EnergyReleased = 200000000 eV * (PresentNeutrons*NeutronAbsorptionProbability())
		//0.000000000032 = Wsec
		//1 eV = 0.00000000000000000016 Wsec

		public bool TryInsertRod(HandApply interaction)
		{


			if (Validations.HasItemTrait(interaction.UsedObject, CommonTraits.Instance.ReactorRod))
			{
				var Rod = interaction.UsedObject.gameObject.GetComponent<ReactorChamberRod>();
				int pos = Array.IndexOf(ReactorRods, null);
				if (pos > -1)
				{
					var engineStarter = Rod as EngineStarter;
					if (engineStarter != null)
					{
						if (ConnectedConsoles.Count == 0)
						{
							Chat.AddExamineMsgFromServer(interaction.Performer,
								" The hole for the starter rod seems to be closed, Seems like you need to hook it up to a console for it to open ");
							return true;
						}
					}

					ReactorRods[pos] = Rod;
					var EmptySlot = RodStorage.GetIndexedItemSlot(pos);
					Inventory.ServerTransfer(interaction.HandSlot, EmptySlot);
					var fuelRod = Rod as FuelRod;
					if (fuelRod != null)
					{
						ReactorFuelRods.Add(fuelRod);
					}


					if (engineStarter != null)
					{
						ReactorEngineStarters.Add(engineStarter);
					}
				}

				return true;
			}

			return false;
		}

		public bool TryInsertPipe(HandApply interaction)
		{
			if (Validations.HasItemTrait(interaction.UsedObject, PipeItemTrait))
			{
				var EmptySlot = PipeStorage.GetIndexedItemSlot(0);
				if (EmptySlot.Item == null)
				{
					Inventory.ServerTransfer(interaction.HandSlot, EmptySlot);
					PoppedPipes = false;
				}

				return true;
			}

			return false;
		}

		public bool TryDeconstructCore(HandApply interaction)
		{
			if (Validations.HasItemTrait(interaction.UsedObject, CommonTraits.Instance.Welder) &&
			    MeltedDown == false)
			{
				if (ReactorRods.All(x => x == null))
				{
					ToolUtils.ServerUseToolWithActionMessages(interaction, 10,
						"You start to deconstruct the empty core...",
						$"{interaction.Performer.ExpensiveName()} starts to deconstruct the empty core...",
						"You deconstruct the empty core.",
						$"{interaction.Performer.ExpensiveName()} deconstruct the empty core.",
						() => { _ = Despawn.ServerSingle(gameObject); });
				}
				else
				{
					Chat.AddExamineMsgFromServer(interaction.Performer,
						"The inserted rods make it impossible to deconstruct");
				}

				return true;
			}

			return false;
		}

		public bool TryAxeCore(HandApply interaction)
		{
			if (Validations.HasItemTrait(interaction.UsedObject, CommonTraits.Instance.Pickaxe) &&
			    MeltedDown == true)
			{
				ToolUtils.ServerUseToolWithActionMessages(interaction, 10,
					"You start to hack away at the molten core...",
					$"{interaction.Performer.ExpensiveName()} starts to hack away at the molten core...",
					"You break the molten core to pieces.",
					$"{interaction.Performer.ExpensiveName()} breaks the molten core to pieces.",
					() => { _ = Despawn.ServerSingle(gameObject); });
				return true;
			}

			return false;
		}

		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side, AllowTelekinesis: false) == false) return false;
			return true;
		}


		public void ServerPerformInteraction(HandApply interaction)
		{
			if (interaction.UsedObject != null)
			{
				if (TryInsertRod(interaction)) return;
				if (TryInsertPipe(interaction)) return;
				if (TryDeconstructCore(interaction)) return;
				if (TryAxeCore(interaction)) return;

				//slam control rods in
				if (MeltedDown == false)
				{
					ControlRodDepthPercentage = 1;
				}
			}
			else
			{
				//pull out rod

				for (int i = ReactorRods.Length; i-- > 0;)
				{
					if (ReactorRods[i] != null)
					{
						var Rod = ReactorRods[i];
						var fuelRod = Rod as FuelRod;
						if (fuelRod != null)
						{
							ReactorFuelRods.Remove(fuelRod);
						}

						var engineStarter = Rod as EngineStarter;
						if (engineStarter != null)
						{
							ReactorEngineStarters.Remove(engineStarter);
						}

						ReactorRods[i] = null;
						var EmptySlot = RodStorage.GetIndexedItemSlot(i);
						Inventory.ServerTransfer(EmptySlot, interaction.HandSlot);
						return;
					}
				}
			}
		}

		#region Multitool Interaction

		public List<ReactorControlConsole> ConnectedConsoles = new List<ReactorControlConsole>();

		public MultitoolConnectionType ConType => MultitoolConnectionType.ReactorChamber;
		public bool MultiMaster => false;
		int IMultitoolMasterable.MaxDistance => 30;

		#endregion
	}
}