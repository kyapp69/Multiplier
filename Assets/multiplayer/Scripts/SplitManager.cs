﻿using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using Common;
using SinglePlayer;


namespace MultiPlayer {
	[System.Serializable]
	public struct SplitGroup {
		public GameUnit ownerUnit;
		public GameUnit splitUnit;
		public float elapsedTime;
		public Vector3 rotationVector;
		public float splitFactor;
		public Vector3 origin;

		public SplitGroup(GameUnit ownerUnit, GameUnit splitUnit, float angle, float splitFactor) {
			this.ownerUnit = ownerUnit;
			this.splitUnit = splitUnit;
			this.elapsedTime = 0f;
			this.origin = ownerUnit.gameObject.transform.position;
			this.splitFactor = splitFactor;

			SpawnRange range = this.ownerUnit.GetComponentInChildren<SpawnRange>();
			this.rotationVector = Quaternion.Euler(0f, angle, 0f) * (Vector3.one * range.radius);
			this.rotationVector.y = 0f;

			UnityEngine.AI.NavMeshAgent agent = this.ownerUnit.GetComponent<UnityEngine.AI.NavMeshAgent>();
			if (agent != null) {
				agent.ResetPath();
				agent.Stop();
			}
			agent = this.splitUnit.GetComponent<UnityEngine.AI.NavMeshAgent>();
			if (agent != null) {
				agent.ResetPath();
				agent.Stop();
			}

			NetworkTransform transform = this.ownerUnit.GetComponent<NetworkTransform>();
			if (transform != null) {
				transform.transformSyncMode = NetworkTransform.TransformSyncMode.SyncNone;
			}
			transform = this.splitUnit.GetComponent<NetworkTransform>();
			if (transform != null) {
				transform.transformSyncMode = NetworkTransform.TransformSyncMode.SyncNone;
			}
		}

		public void Update() {
			this.ownerUnit.isSelected = false;
			this.splitUnit.isSelected = false;

			Vector3 pos = Vector3.Lerp(this.origin, this.origin + this.rotationVector, this.elapsedTime);
			if (this.ownerUnit == null || this.ownerUnit.gameObject == null) {
				this.elapsedTime = 1f;
				return;
			}
			this.ownerUnit.gameObject.transform.position = pos;
			pos = Vector3.Lerp(this.origin, this.origin - this.rotationVector, this.elapsedTime);
			if (this.splitUnit == null || this.splitUnit.gameObject == null) {
				this.elapsedTime = 1f;
				return;
			}
			this.splitUnit.gameObject.transform.position = pos;
		}

		public void Stop() {
			UnityEngine.AI.NavMeshAgent agent = null;
			if (this.ownerUnit != null) {
				this.ownerUnit.isSplitting = false;
				agent = this.ownerUnit.GetComponent<UnityEngine.AI.NavMeshAgent>();
				if (agent != null) {
					agent.Resume();
				}
			}

			if (this.splitUnit != null) {
				this.splitUnit.isSplitting = false;
				agent = this.splitUnit.GetComponent<UnityEngine.AI.NavMeshAgent>();
				if (agent != null) {
					agent.Resume();
				}
			}

			NetworkTransform transform = this.ownerUnit.GetComponent<NetworkTransform>();
			if (transform != null) {
				transform.transformSyncMode = NetworkTransform.TransformSyncMode.SyncTransform;
			}
			transform = this.splitUnit.GetComponent<NetworkTransform>();
			if (transform != null) {
				transform.transformSyncMode = NetworkTransform.TransformSyncMode.SyncTransform;
			}
		}
	};


	public class SplitManager : NetworkBehaviour {
		[SerializeField]
		public List<SplitGroup> splitGroupList;
		[SerializeField]
		public List<SplitGroup> removeList;
		[SerializeField]
		public SelectionManager selectionManager;
		[SerializeField]
		public UnitAttributes unitAttributes;
		[SerializeField]
		public Spawner spawner;
		public GameObject gameUnitPrefab;
		public Transform unitParent;
		public int maxUnitCount;
		public GlobalManager globalManagerObject;

		//Split Manager is designed to streamline the creation of new game units.
		//To achieve this, there needs to be two different array list that keeps track of all the creations, called Split Groups.
		//One keeps track of the Split Groups, the other removes them from the tracking list.

		public void Start() {
			if (!this.hasAuthority) {
				return;
			}

			if (this.splitGroupList == null) {
				this.splitGroupList = new List<SplitGroup>();
			}
			if (this.selectionManager == null) {
				GameObject[] managers = GameObject.FindGameObjectsWithTag("SelectionManager");
				foreach (GameObject manager in managers) {
					SelectionManager select = manager.GetComponent<SelectionManager>();
					if (select != null && select.hasAuthority) {
						this.selectionManager = select;
						break;
					}
				}
				if (this.selectionManager == null) {
					Debug.LogError("Cannot find Selection Manager. Aborting");
				}
			}
			if (this.unitAttributes == null) {
				GameObject[] attributes = GameObject.FindGameObjectsWithTag("UnitAttributes");
				foreach (GameObject attribute in attributes) {
					UnitAttributes attr = attribute.GetComponent<UnitAttributes>();
					if (attr != null && attr.hasAuthority) {
						this.unitAttributes = attr;
						break;
					}
				}
				if (this.unitAttributes == null) {
					Debug.LogError("Split Manager: Unit Attributes Tracker is null. Please check.");
				}
			}
			if (this.spawner == null) {
				GameObject[] spawners = GameObject.FindGameObjectsWithTag("Spawner");
				foreach (GameObject obj in spawners) {
					Spawner spawner = obj.GetComponent<Spawner>();
					if (spawner != null && spawner.hasAuthority) {
						this.spawner = spawner;
						break;
					}
				}
				if (this.spawner == null) {
					Debug.LogError("Spawner is never set. Please check.");
				}
			}
			if (this.unitParent == null) {
				this.unitParent = new GameObject("Units Parent").transform;
                NetworkIdentity identity = this.unitParent.gameObject.AddComponent<NetworkIdentity>();
                identity.localPlayerAuthority = true;
                ClientScene.RegisterPrefab(this.unitParent.gameObject);
				this.unitParent.SetParent(this.transform);
				if (this.selectionManager != null) {
					foreach (GameObject obj in this.selectionManager.allObjects) {
						obj.transform.SetParent(this.unitParent);
					}
				}
				NetworkIdentity ident = this.GetComponent<NetworkIdentity>();
				if (ident != null) {
					ident.localPlayerAuthority = true;
					CmdSpawn(this.unitParent.gameObject);
					Debug.Log("Spawning a new unit parent with client authority owner.");
				}
				else {
					Debug.LogError("Check to make sure this is created in the spawner.");
				}
			}
		}

		[Command]
		public void CmdSpawn(GameObject obj) {
			if (obj != null) {
				NetworkServer.SpawnWithClientAuthority(obj, this.connectionToClient);
			}
		}

		public void Update() {
			if (!this.hasAuthority) {
				return;
			}

			//When the player starts the action to split a game unit into two, it takes in all the selected game units
			//one by one, and splits them individually.
			if (Input.GetKeyDown(KeyCode.S)) {
				this.maxUnitCount = SelectionManager.MAX_UNIT_COUNT;
				if (this.globalManagerObject != null) {
					this.maxUnitCount = this.globalManagerObject.playerMaxUnitCount;
					this.selectionManager.currentMaxUnitCount = this.globalManagerObject.playerMaxUnitCount;
				}
				if (this.selectionManager != null) {
					AddingNewSplitGroup();
				}
			}
			UpdateSplitGroup();
		}

		public void UpdateSplitGroup() {
			if (this.splitGroupList != null && this.splitGroupList.Count > 0) {
				for (int i = 0; i < this.splitGroupList.Count; i++) {
					SplitGroup group = this.splitGroupList[i];
					if (group.elapsedTime >= 1f) {
						group.Stop();
						Increment(group.ownerUnit);
						Decrement(group.ownerUnit);
						Increment(group.splitUnit);
						Decrement(group.splitUnit);
						if (group.splitUnit != null && !this.selectionManager.allObjects.Contains(group.splitUnit.gameObject)) {
							this.selectionManager.allObjects.Add(group.splitUnit.gameObject);
						}
						if (!this.selectionManager.allObjects.Contains(group.ownerUnit.gameObject)) {
							this.selectionManager.allObjects.Add(group.ownerUnit.gameObject);
						}
						if (group.ownerUnit.transform.parent == null || !group.ownerUnit.transform.parent.Equals(this.unitParent)) {
							group.ownerUnit.transform.SetParent(this.unitParent);
						}
						if (group.splitUnit.transform.parent == null || !group.splitUnit.transform.parent.Equals(this.unitParent)) {
							group.splitUnit.transform.SetParent(this.unitParent);
						}
						this.removeList.Add(group);

											}
					else {
						//Some weird C# language design...
						group.Update();
						group.elapsedTime += Time.deltaTime / group.splitFactor;
						this.splitGroupList[i] = group;
					}
				}
			}

			if (this.removeList != null && this.removeList.Count > 0) {
				foreach (SplitGroup group in this.removeList) {
					this.splitGroupList.Remove(group);
				}
				this.removeList.Clear();
			}
		}

		private void AddingNewSplitGroup() {
			foreach (GameObject obj in this.selectionManager.selectedObjects) {
				if (obj == null) {
					this.selectionManager.removeList.Add(obj);
					continue;
				}
				GameUnit objUnit = obj.GetComponent<GameUnit>();
				if (objUnit.level == 1 && this.unitParent.transform.childCount < this.maxUnitCount) {
					CmdSplit(obj, objUnit.hasAuthority);
				}
			}
			return;
		}

		[Command]
		public void CmdSplit(GameObject obj, bool hasAuthority) {
			GameUnit unit = obj.GetComponent<GameUnit>();
			if (unit.unitAttributes == null) {
				if (this.unitAttributes != null) {
					unit.unitAttributes = this.unitAttributes;
				}
				else {
					Debug.LogError("Definitely something is wrong here with unit attributes.");
				}
			}

			if (unit.isSplitting) {
				return;
			}

			unit.isSplitting = true;

			//This is profoundly one of the hardest puzzles I had tackled. Non-player object spawning non-player object.
			//Instead of the usual spawning design used in the Spawner script, the spawning codes here are swapped around.
			//In Spawner, you would called on NetworkServer.SpawnWithClientAuthority() in the [ClientRpc]. Here, it's in [Command].
			//I am guessing it has to do with how player objects and non-player objects interact with UNET.
			GameObject split = MonoBehaviour.Instantiate(unit.gameObject) as GameObject;

			//Setting the newly split game unit's name to something else.
			split.name = "GameUnit " + this.unitParent.transform.childCount.ToString();

			//Setting owner's parent as the same for splits.
			split.transform.SetParent(obj.transform.parent);

			GameUnit splitUnit = split.GetComponent<GameUnit>();
			if (splitUnit != null) {
				Copy(unit, splitUnit);
			}

			NetworkIdentity managerIdentity = this.GetComponent<NetworkIdentity>();
			NetworkServer.SpawnWithClientAuthority(split, managerIdentity.clientAuthorityOwner);
			float angle = UnityEngine.Random.Range(-180f, 180f);

			RpcSplit(obj, split, angle, hasAuthority, this.unitAttributes.splitPrefabFactor);
		}

		[ClientRpc]
		public void RpcSplit(GameObject obj, GameObject split, float angle, bool hasAuthority, float splitFactor) {
			//We do not call on NetworkServer methods here. This is used only to sync up with the original game unit for all clients.
			//This includes adding the newly spawned game unit into the Selection Manager that handles keeping track of all game units.
			if (obj == null || split == null) {
				return;
			}

			GameUnit original = obj.GetComponent<GameUnit>();
			GameUnit copy = split.GetComponent<GameUnit>();

			if (original.unitAttributes == null) {
				GameObject attrObj = GameObject.FindGameObjectWithTag("UnitAttributes");
				if (obj != null) {
					original.unitAttributes = attrObj.GetComponent<UnitAttributes>();
					if (original.unitAttributes == null) {
						Debug.LogError("Unit attributes are missing from original unit.");
					}
				}
			}
			if (copy.unitAttributes == null) {
				GameObject attrObj = GameObject.FindGameObjectWithTag("UnitAttributes");
				if (obj != null) {
					copy.unitAttributes = attrObj.GetComponent<UnitAttributes>();
					if (copy.unitAttributes == null) {
						Debug.LogError("Unit attributes are missing from copy unit.");
					}
				}
			}

			UnityEngine.AI.NavMeshAgent originalAgent = obj.GetComponent<UnityEngine.AI.NavMeshAgent>();
			originalAgent.ResetPath();
			UnityEngine.AI.NavMeshAgent copyAgent = split.GetComponent<UnityEngine.AI.NavMeshAgent>();
			copyAgent.ResetPath();

			GameObject[] splitManagerGroup = GameObject.FindGameObjectsWithTag("SplitManager");
			if (splitManagerGroup.Length > 0) {
				for (int i = 0; i < splitManagerGroup.Length; i++) {
					SplitManager manager = splitManagerGroup[i].GetComponent<SplitManager>();
					if (manager != null && manager.hasAuthority == hasAuthority) {
						manager.splitGroupList.Add(new SplitGroup(original, copy, angle, splitFactor));
						if (manager.selectionManager == null) {
							GameObject[] objs = GameObject.FindGameObjectsWithTag("SelectionManager");
							foreach (GameObject select in objs) {
								SelectionManager selectManager = select.GetComponent<SelectionManager>();
								if (selectManager.hasAuthority) {
									manager.selectionManager = selectManager;
								}
							}
						}
						manager.selectionManager.allObjects.Add(split);
					}
				}
			}
		}

		[ServerCallback]
		private static void Copy(GameUnit original, GameUnit copy) {
			copy.isSelected = original.isSelected;
			copy.isSplitting = original.isSplitting;
			copy.isMerging = original.isMerging;

			copy.transform.position = original.transform.position;
			copy.transform.rotation = original.transform.rotation;
			copy.transform.localScale = original.transform.localScale;
			copy.oldTargetPosition = original.oldTargetPosition = -Vector3.one * 9999f;
			copy.isDirected = original.isDirected = false;

			copy.level = original.level;
			copy.previousLevel = original.previousLevel;
			copy.currentHealth = original.currentHealth;
			copy.maxHealth = original.maxHealth;
			if (copy.currentHealth > copy.maxHealth) {
				copy.currentHealth = copy.maxHealth;
			}
			if (original.currentHealth > original.maxHealth) {
				original.currentHealth = original.maxHealth;
			}
			//copy.recoverCooldown = original.recoverCooldown;
			copy.recoverCounter = original.recoverCounter = 1f;
			copy.speed = original.speed;
			copy.attackCooldown = original.attackCooldown;
			copy.attackCooldownCounter = original.attackCooldownCounter = 0;
			copy.attackPower = original.attackPower;

			copy.unitAttributes = original.unitAttributes;
			copy.teamColorValue = original.teamColorValue;

			original.SetTeamColor(original.teamColorValue);
			copy.SetTeamColor(copy.teamColorValue);
			copy.teamFaction = original.teamFaction;
		}

		[ServerCallback]
		private static void Increment(GameUnit unit) {
			unit.isSelected = !unit.isSelected;
			unit.isDirected = !unit.isDirected;
			unit.isSplitting = !unit.isSplitting;
			unit.isMerging = !unit.isMerging;
			unit.currentHealth++;
			unit.maxHealth++;
			unit.attackPower++;
			unit.attackCooldown++;
			unit.speed++;
			//unit.recoverCooldown++;
			unit.level++;
			unit.previousLevel++;
			unit.teamColorValue = (unit.teamColorValue + 1) % 3;
		}

		[ServerCallback]
		private static void Decrement(GameUnit unit) {
			unit.isSelected = !unit.isSelected;
			unit.isDirected = !unit.isDirected;
			unit.isSplitting = !unit.isSplitting;
			unit.isMerging = !unit.isMerging;
			unit.currentHealth--;
			unit.maxHealth--;
			unit.attackPower--;
			unit.attackCooldown--;
			unit.speed--;
			//unit.recoverCooldown--;
			unit.level--;
			unit.previousLevel--;
			unit.teamColorValue = (unit.teamColorValue + 2) % 3;
		}
	}
}
