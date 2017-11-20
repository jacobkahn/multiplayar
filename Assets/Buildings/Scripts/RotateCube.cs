using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateCube : MonoBehaviour {

	public float speed = 2.0f; 

	// Use this for initialization
	void Start () {
		transform.rotation = Quaternion.Euler (0, -Input.compass.magneticHeading, 0);
	}
	
	// Update is called once per frame
	void Update () {
		transform.rotation = Quaternion.Euler (0, -Input.compass.magneticHeading, 0);

//		if (Input.touchCount == 1) {
//			Touch touch = Input.GetTouch (0);
//
//			if (touch.phase == TouchPhase.Moved) {
//				float h = speed * touch.deltaPosition.x;
//				transform.Rotate (0, -h, 0, Space.World); 
//			}
//				
//		}
	}
}
