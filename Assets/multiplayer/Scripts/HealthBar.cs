﻿using UnityEngine;
using System.Collections.Generic;
using MultiPlayer;


public class HealthBar : MonoBehaviour {
	public GameUnit unit;
	public Camera minimapCamera;
	public Vector3 viewportPosition;

	public void Start() {
		if (this.minimapCamera == null) {
			GameObject obj = GameObject.FindGameObjectWithTag("Minimap");
			if (obj != null) {
				if (obj.activeSelf || obj.activeInHierarchy) {
					this.minimapCamera = obj.GetComponent<Camera>();
					if (this.minimapCamera == null) {
						Debug.LogError("HealthBar: This failed to initialize minimap camera.");
					}
					if (!this.minimapCamera.isActiveAndEnabled || !this.minimapCamera.enabled) {
						this.minimapCamera.enabled = true;
					}
				}
			}
		}
	}

	public void OnGUI() {
		if (this.minimapCamera != null) {
			GUIStyle style = new GUIStyle();
			style.normal.textColor = Color.black;
			style.alignment = TextAnchor.MiddleCenter;
			Vector3 healthPosition = Camera.main.WorldToScreenPoint(this.gameObject.transform.position);
			this.viewportPosition = Camera.main.ScreenToViewportPoint(new Vector3(healthPosition.x, healthPosition.y + 30f));
			if (!this.minimapCamera.rect.Contains(this.viewportPosition)) {
				Rect healthRect = new Rect(healthPosition.x - 50f, (Screen.height - healthPosition.y) - 45f, 100f, 25f);
				GUI.Label(healthRect, unit.currentHealth.ToString() + "/" + unit.maxHealth.ToString(), style);
			}
		}
		else {
			GameObject obj = GameObject.FindGameObjectWithTag("Minimap");
			if (obj != null) {
				if (obj.activeSelf || obj.activeInHierarchy) {
					this.minimapCamera = obj.GetComponent<Camera>();
					if (this.minimapCamera == null) {
						return;
					}
					if (!this.minimapCamera.isActiveAndEnabled || !this.minimapCamera.enabled) {
						this.minimapCamera.enabled = true;
					}
				}
			}
		}
	}
}
