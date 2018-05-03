﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections.Generic;
using SinglePlayer;


namespace MultiPlayer {
	public class SelectionManager : NetworkBehaviour {
		public const int MAX_UNIT_COUNT = 50;

		public List<GameObject> selectedObjects;
		public List<GameObject> allObjects;
		public List<GameObject> removeList; 

		public Rect selectionBox;
		public NetworkConnection authorityOwner;
		public Camera minimapCamera;
		public Vector3 initialClick;
		public Vector3 screenPoint;

		public Text unitCountText;

		public int currentMaxUnitCount = MAX_UNIT_COUNT;
		public bool isSelecting;
		public bool isBoxSelecting;
		public bool isDead;

		public override void OnStartClient() {
			base.OnStartClient();

			if (this.minimapCamera == null) {
				GameObject obj = GameObject.FindGameObjectWithTag("Minimap");
				if (obj != null) {
					this.minimapCamera = obj.GetComponent<Camera>();
					if (this.minimapCamera == null) {
						Debug.LogError("Failure to obtain minimap camera.");
					}
				}
			}
		}

		void Start() {
			//If you need to use a different design instead of checking for hasAuthority, then it means
			//you will have to figure out how to do what you need to do, and this example will not
			//be sufficient enough to teach you more than given.
			if (!this.hasAuthority) {
				return;
			}

			this.isDead = false;

			this.InitializeList();
			this.selectionBox = new Rect();

			GameObject[] selectionManagers = GameObject.FindGameObjectsWithTag("SelectionManager");
			foreach (GameObject manager in selectionManagers) {
				SelectionManager selectManager = manager.GetComponent<SelectionManager>();
				if (selectManager == null || !selectManager.hasAuthority) {
					continue;
				}

				GameObject[] units = GameObject.FindGameObjectsWithTag("Unit");
				foreach (GameObject unit in units) {
					GameUnit gameUnit = unit.GetComponent<GameUnit>();
					if (gameUnit != null && !gameUnit.hasAuthority) {
						continue;
					}
					selectManager.allObjects.Add(unit);
				}
			}
		}

		void Update() {
			if (!this.hasAuthority) {
				return;
			}
			if (this.minimapCamera == null) {
				return;
			}
			if (this.allObjects.Count <= 0) {
				if (!this.isDead) {
										AIManager.Instance.startAIFlag = false;
					this.isDead = true;
				}
				return;
			}

			//This handles all the input actions the player has done in the minimap.
			this.screenPoint = Camera.main.ScreenToViewportPoint(Input.mousePosition);
			if (this.minimapCamera.rect.Contains(this.screenPoint) && Input.GetMouseButtonDown(1)) {
				if (this.selectedObjects.Count > 0) {
					float mainX = (this.screenPoint.x - this.minimapCamera.rect.xMin) / (1.0f - this.minimapCamera.rect.xMin);
					float mainY = (this.screenPoint.y) / (this.minimapCamera.rect.yMax);
					Vector3 minimapScreenPoint = new Vector3(mainX, mainY, 0f);
					foreach (GameObject obj in this.selectedObjects) {
						GameUnit unit = obj.GetComponent<GameUnit>();
						if (unit != null) {
							unit.CastRay(true, minimapScreenPoint, this.minimapCamera);
						}
					}
				}
			}
			else {
				if (this.minimapCamera.rect.Contains(this.screenPoint)) {
					return;
				}
				//This handles all the input actions the player has done to box select in the game.
				//Currently, it doesn't handle clicking to select.
				if (Input.GetMouseButtonDown(0)) {
					if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
						ClearSelectObjects();
					}
					this.isSelecting = true;
					this.initialClick = Input.mousePosition;
				}
				else if (Input.GetMouseButtonUp(0)) {
					if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
						ClearSelectObjects();
					}
					SelectObjectAtPoint();
					SelectObjectsInRect();
					SelectObjects();
					this.isSelecting = false;
					this.isBoxSelecting = false;
					this.initialClick = -Vector3.one * 9999f;
				}
			}

			if (this.isSelecting && Input.GetMouseButton(0)) {
				if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) {
					this.isBoxSelecting = true;
				}
				this.selectionBox.Set(this.initialClick.x, Screen.height - this.initialClick.y, Input.mousePosition.x - this.initialClick.x, (Screen.height - Input.mousePosition.y) - (Screen.height - this.initialClick.y));
				if (this.selectionBox.width < 0) {
					this.selectionBox.x += this.selectionBox.width;
					this.selectionBox.width *= -1f;
				}
				if (this.selectionBox.height < 0) {
					this.selectionBox.y += this.selectionBox.height;
					this.selectionBox.height *= -1f;
				}
				TempRectSelectObjects();
			}

			foreach (GameObject obj in this.allObjects) {
				if (obj == null && !this.removeList.Contains(obj)) {
					this.removeList.Add(obj);
				}
			}

			for (int i = 0; i < this.selectedObjects.Count; i++) {
				if (this.selectedObjects[i] == null) {
					this.selectedObjects.RemoveAt(i);
				}
			}

			if (this.removeList.Count > 0) {
				foreach (GameObject obj in this.removeList) {
					if (this.allObjects.Contains(obj)) {
						this.allObjects.Remove(obj);
					}
				}
				foreach (GameObject obj in this.removeList) {
					CmdDestroy(obj);
				}
				this.removeList.Clear();
			}
		}

		void OnGUI() {
			if (Taskbar.Instance != null) {
				Taskbar.Instance.taskbarText.text = "Unit Count / Max Unit Count : " + this.allObjects.Count + "/" + this.currentMaxUnitCount;
			}
		}

		public void InitializeList() {
			if (this.selectedObjects == null) {
				this.selectedObjects = new List<GameObject>(100);
			}

			if (this.allObjects == null) {
				this.allObjects = new List<GameObject>(100);
			}
		}

		public void AddToRemoveList(GameObject obj) {
			if (!this.removeList.Contains(obj)) {
				this.removeList.Add(obj);
			}
		}

		[Command]
		public void CmdDestroy(GameObject obj) {
			NetworkServer.Destroy(obj);
		}


		//-----------   Private class methods may all need refactoring   --------------------

		private void TempRectSelectObjects() {
			foreach (GameObject obj in this.allObjects) {
				if (obj == null) {
					//Because merging units will actually destroy units (as a resource), we now added a check to make sure
					//we don't call on NULL referenced objects, and remove them from the list.
					this.removeList.Add(obj);
					continue;
				}
				Vector3 projectedPosition = Camera.main.WorldToScreenPoint(obj.transform.position);
				projectedPosition.y = Screen.height - projectedPosition.y;
				GameUnit unit = obj.GetComponent<GameUnit>();
				if (this.selectionBox.Contains(projectedPosition)) {
					unit.isSelected = true;
				}
			}
		}

		private void SelectObjects() {
			foreach (GameObject obj in this.allObjects) {
				if (obj == null) {
					this.removeList.Add(obj);
					continue;
				}
				GameUnit unit = obj.GetComponent<GameUnit>();
				if (unit != null) {
					if (this.selectedObjects.Contains(obj)) {
						unit.isSelected = true;
					}
				}
			}
		}

		private void SelectObjectsInRect() {
			foreach (GameObject obj in this.allObjects) {
				if (obj == null) {
					continue;
				}
				GameUnit unit = obj.GetComponent<GameUnit>();
				if (unit != null) {
					if (this.isBoxSelecting) {
						Vector3 projectedPosition = Camera.main.WorldToScreenPoint(obj.transform.position);
						projectedPosition.y = Screen.height - projectedPosition.y;
						if (this.selectionBox.Contains(projectedPosition)) {
							if (this.selectedObjects.Contains(obj)) {
								unit.isSelected = false;
								this.selectedObjects.Remove(obj);
							}
							else {
								unit.isSelected = true;
								this.selectedObjects.Add(obj);
							}
						}
					}
					else {
						if (unit.isSelected) {
							if (!this.selectedObjects.Contains(obj)) {
								this.selectedObjects.Add(obj);
							}
						}
					}
				}
			}
		}

		private void ClearSelectObjects() {
			foreach (GameObject obj in this.selectedObjects) {
				if (obj == null) {
					continue;
				}
				GameUnit unit = obj.GetComponent<GameUnit>();
				unit.isSelected = false;
			}
			this.selectedObjects.Clear();
		}

		private void SelectObjectAtPoint() {
			Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
			RaycastHit[] hits = Physics.RaycastAll(ray);
			foreach (RaycastHit hit in hits) {
				GameObject obj = hit.collider.gameObject;
				if (obj.tag.Equals("Unit")) {
					GameUnit unit = obj.GetComponent<GameUnit>();
					if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) {
						if (this.allObjects.Contains(obj)) {
							if (!this.selectedObjects.Contains(obj)) {
								unit.isSelected = true;
								this.selectedObjects.Add(obj);
							}
							else if (this.selectedObjects.Contains(obj)) {
								unit.isSelected = false;
								this.selectedObjects.Remove(obj);
							}
						}
					}
					else {
						if (unit != null) {
							unit.isSelected = true;
							this.selectedObjects.Add(obj);
						}
					}
				}
			}
		}
	}
}