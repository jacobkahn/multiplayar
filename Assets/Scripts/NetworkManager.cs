using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine;

/* NetworkManager manages synchronization of all the objects 
 * in the scene and communicating with the server
 */
public class NetworkManager : MonoBehaviour {
  
	/** Server endpoints **/ 
	public string address = "http://multiplayar.me";
	public string anchorEndpoint = "/anchor";
	public string objectEndpoint = "/object";
	public string syncEndpoint = "/sync";

	public Dictionary<string, GameObject> objectMap = new Dictionary<string, GameObject>();
	public Dictionary<int, string> reverseObjectMap = new Dictionary<int, string> ();
	public GameObject mainCamera;
	public GameObject hitCubePrefab;
	public GameObject anchor;

	private string userId;
	private bool synced; 
	private GameObject newCube;
	private int frames;


	[System.Serializable] 
	public class VectorInformation {
		public float x;
		public float y;
		public float z;
		public string id;
	}

//	[System.Serializable]
//	public class TransformData {
//		public List<VectorInformation> v;
//	}

	[System.Serializable]
	public class RootObject {
		public List<VectorInformation> users;
		public List<VectorInformation> objects;
	
		public static RootObject CreateFromJSON (string j) {
			return JsonUtility.FromJson<RootObject> (j);
		}
	}
		
	void Start () {
		Debug.Log("Network Manager started with a connection to " + address);
		userId = "";
		frames = 0;
		synced = false;
	}
	
	void Update () {
		frames++;
		if (frames % 120 == 0) {
			SyncWorld ();
			frames = 0;
		}
	}
		 
	/* Each GameObject has an associated server-assigned string objectId. 
	 * The reverseObjectMap maps instanceId to objectId, and is thus used 
	 * to fetch the objectId for a GameObject. Note: empty string should 
	 * only be returned when the GameObject is first created.
	 */
	public string GetObjectId (GameObject obj) {
		if(reverseObjectMap.ContainsKey(obj.GetInstanceID())) {
			return reverseObjectMap[obj.GetInstanceID()];
		} else {
			return "";
		}
	}


	/* Retrieves the input GameObject's offset from the world anchor
	 */
	private Vector3 OffsetFromAnchor(GameObject obj) {
		Vector3 anchorPos = anchor.transform.position;
		Vector3 objPos = obj.transform.position;
		Vector3 offset = anchorPos - objPos;
		return offset;
	}
  
	/*
	 */
	public void SendOffset() {
		if (synced) {
			return;
		}

		Vector3 offset = OffsetFromAnchor (mainCamera);

//		Debug.Log (string.Format ("Offset: x:{0:0.######} y:{1:0.######} z:{2:0.######}", offset.x, offset.y, offset.z));
//		Debug.Log (offset);

		WWWForm form = new WWWForm();
		form.AddField("x", offset.x.ToString());
		form.AddField("y", offset.y.ToString());
		form.AddField("z", offset.z.ToString());
		if (userId.Length != 0) {
			form.AddField("userId", userId);
		}

		newCube = Instantiate (hitCubePrefab, new Vector3 (0, 0, 0), Quaternion.identity);
		newCube.SetActive (true);
		anchor.SetActive (false);

		TransformManager t = newCube.AddComponent<TransformManager> ();
		t.nm = this;

		synced = true;

		WWW www = new WWW(address + anchorEndpoint, form);
		StartCoroutine(WaitForUserId(www));
	}

	/* Method is called if the user added an object to the environment 
	 * OR if an object's position has changed. Not sending an ObjectId 
	 * signals the server that this is a brand new object.
	 */
	public void SendObject(string objectId, GameObject obj) {
//		Vector3 offset = OffsetFromAnchor (obj);
		GameObject temp = new GameObject ();
		temp.transform.RotateAround (anchor.transform.position, Vector3.up, -1.0f * anchor.transform.rotation.eulerAngles.y);
		temp.transform.position = obj.transform.position - anchor.transform.position;
		Vector3 offset = temp.transform.position;

		WWWForm form = new WWWForm ();
		form.AddField("x", offset.x.ToString());
		form.AddField("y", offset.y.ToString());
		form.AddField("z", offset.z.ToString());
		if (objectId.Length != 0) {
			form.AddField("objectId", objectId);
		}

		WWW www = new WWW(address + objectEndpoint, form);
		StartCoroutine(WaitForObjectId(www, obj));
	}


	/* Get state of the world from server
	 */
	public void SyncWorld() {
		WWW www = new WWW (address + syncEndpoint);
		StartCoroutine (WaitForWorldSync (www));
	}
  
	/* Get userId from server
	 */
	public IEnumerator WaitForUserId(WWW www) {
		yield return www;

		if (www.error != null) {
			Debug.Log("WWW Error: "+ www.error);
			yield break;
		}

		userId = www.text;
	}

	/* If in SendObject() we pushed information about a brand new object,
	 * the server will respond with an objectId which we use to update our 
	 * objectMap and reverseObjectMap.
	 */
	public IEnumerator WaitForObjectId(WWW www, GameObject obj) {
		yield return www;
		
		if (www.error != null) {
			Debug.Log ("WWW Error: " + www.error);
			yield break;
		}
				
		string objectId = www.text;

		if (!objectMap.ContainsKey (objectId)) {
			objectMap.Add (objectId, obj);	
			reverseObjectMap.Add (obj.GetInstanceID (), objectId);
		} 
		TransformManager t = newCube.GetComponent<TransformManager> ();
		t.objectId = objectId;

	}

	/* Retrieves the server maintained state of the world,
	 * for known GameObjects, updates their positions. For new objects,
	 * places them into the world and then removes UnityARHitTestExample script
	 * which prevents the user from modifying them. This way, a user cannot move 
	 * objects created by another user.
	 */ 
	public IEnumerator WaitForWorldSync(WWW www) {
		yield return www;

		if (www.error != null) {
			Debug.Log ("WWW Error: " + www.error);
			yield break;
		}
			
		RootObject root = RootObject.CreateFromJSON(www.text);

		Debug.Log ("WORLD SYNC: " + root.objects.Count);

		foreach (VectorInformation v in root.objects) {
			string objectId = v.id;
			Vector3 otherPos = new Vector3 (v.x, v.y, v.z);


			if (objectMap.ContainsKey (objectId)) {
				GameObject obj = objectMap [objectId];

				GameObject temp = new GameObject ();
				temp.transform.position = obj.transform.position + anchor.transform.position;
				temp.transform.RotateAround (anchor.transform.position, Vector3.up, anchor.transform.rotation.eulerAngles.y);
				Vector3 newPos = temp.transform.position;

				obj.transform.position = newPos;
			} else {
				GameObject other = Instantiate (hitCubePrefab, anchor.transform.position + otherPos, Quaternion.identity);
				other.transform.RotateAround (anchor.transform.position, Vector3.up, anchor.transform.rotation.eulerAngles.y);

				objectMap.Add (objectId, other);
				reverseObjectMap.Add (other.GetInstanceID (), objectId);

				Component[] scripts = other.GetComponentsInChildren (typeof(UnityEngine.XR.iOS.UnityARHitTestExample), true);
				foreach (UnityEngine.XR.iOS.UnityARHitTestExample s in scripts) {
					Destroy (s);
				}

			}
		}
	}

}
