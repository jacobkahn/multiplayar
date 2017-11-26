using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/* TransformManager is attached to all multiplayer 
 * objects and keeps track of when their state has changed. 
 * Any state changes will signal the TransformManager
 * to trigger the SendObject() function on the NetworkManager
 * for the scene to push updates for this object to the server.
 */
public class TransformManager : MonoBehaviour {

	public string objectId;
	public NetworkManager nm; 
	private Vector3 prevPosition; 

	void Start () {
		objectId = "";
		prevPosition = transform.position;
		nm.SendObject (objectId, this.gameObject);
	}

	void LateUpdate () {
		if (this.gameObject.transform.position != prevPosition && objectId.Length != 0) {

			Debug.Log ("Transform Manager Position: " + this.gameObject.transform.position.ToString ());

			nm.SendObject (objectId, this.gameObject);
			prevPosition = this.gameObject.transform.position;
		}
	}
}
