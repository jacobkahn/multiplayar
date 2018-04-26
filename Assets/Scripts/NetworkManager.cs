using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.Networking;
using UnityEngine.XR.iOS;
using UnityEngine;
using UnityEngine.UI;
using System.Text;

/* NetworkManager manages synchronization of all the objects
 * in the scene and communicating with the server
 */
public class NetworkManager : MonoBehaviour {

	/** Server endpoints **/
	public string address = "http://multiplayar.me";
	public string pointPollEndpoint = "/pointpoll";
	public string anchorEndpoint  = "/anchor";
	public string objectEndpoint = "/object";
	public string imageEndpoint = "/image";
	public string syncEndpoint = "/sync";

	private bool DEBUG = false;

	public Dictionary<string, GameObject> objectMap = new Dictionary<string, GameObject>();
	public Dictionary<int, string> reverseObjectMap = new Dictionary<int, string> ();
	public GameObject mainCamera;
	public GameObject multiplayerObjectPrefabOne;
  	public GameObject multiplayerObjectPrefabTwo;
  	public GameObject electricOrbPrefab;
  	private GameObject anchor;
	private List<Vector3> matchpoints;
	private List<GameObject> targets;
	private List<bool> targetselected;
	private List<bool> targetenabled;
	private Dictionary<Vector2, Vector3> hitPointMap = new Dictionary<Vector2, Vector3> ();
	private Dictionary<Vector3, Vector2> reverseHitPointMap = new Dictionary<Vector3, Vector2> ();

	private bool anchorSelect = false;
	private bool anchorChosen = false;

	private string guid;
	private string userId;
	private bool synced;
	private GameObject multiplayerObject;
	private int frames;
	private bool lockPoints = false;
	private bool pollForAnchor = false;
  private int POINTCLOUDBUFFERSIZE = 20;

	private string syncButtonGameObjectName = "SyncButton";
	private bool readyToSync = false;

	private Sprite unselected;
	private Sprite selected;
	private Queue<Vector3> pointCloudQueue;
	private Vector3[] pointClouds;
	private ParticleSystem.MinMaxGradient ogColor;
	private List<IEnumerator> scaleEnumerator;
	private bool setHeading;
	private float heading;

	private GameObject selectedMultiplayARAnchor;

	private byte[] empty_post_data = Encoding.ASCII.GetBytes("fucapplr");

	[System.Serializable]
	public class PointInformation {
		public float x;
		public float y;

		public PointInformation(float x, float y) {
			this.x = x;
			this.y = y;
		}

		public static PointInformation CreateFromJSON (string j) {
			return JsonUtility.FromJson<PointInformation> (j);
		}
	}

	[System.Serializable]
	public class MatchedPoints {
		public List<PointInformation> points;

		public static MatchedPoints CreateFromJSON (string j) {
			return JsonUtility.FromJson<MatchedPoints> (j);
		}
	}

	[System.Serializable]
	public class VectorInformation {
		public float x;
		public float y;
		public float z;
		public string id;
	}


	[System.Serializable]
	public class RootObject {
		public List<VectorInformation> users;
		public List<VectorInformation> objects;
		public float rotation;

		public static RootObject CreateFromJSON (string j) {
			return JsonUtility.FromJson<RootObject> (j);
		}
	}

	void Start () {
		Debug.Log("Network Manager started with a connection to " + address);
		userId = "";
		frames = 0;
		synced = false;
		guid = System.Guid.NewGuid().ToString();
		matchpoints = new List<Vector3> ();
		targets = new List<GameObject> ();
		SpriteRenderer unselectedR = GameObject.Find ("Unselected").GetComponent<SpriteRenderer> ();
		unselectedR.enabled = false;
		unselected = unselectedR.sprite;
		SpriteRenderer selectedR = GameObject.Find ("Selected").GetComponent<SpriteRenderer> ();
		selectedR.enabled = false;
		selected = selectedR.sprite;
		anchorSelect = true;
		targetselected = new List<bool> ();
		targetenabled = new List<bool> ();
		scaleEnumerator = new List<IEnumerator> ();
		pointCloudQueue = new Queue<Vector3> ();
		UnityARSessionNativeInterface.ARFrameUpdatedEvent += ARFrameUpdated;
		Input.location.Start ();
		setHeading = false;
    	// find the button (if it exists)
  	  	setButtonText("Wait");
	}

  	public void setButtonText(string setText) {
	    GameObject button = GameObject.Find(syncButtonGameObjectName);
	    if (button != null) {
	      	Text text = button.GetComponentInChildren<Text>(true);
	      	if (text != null) {
	        	text.text = setText;
	      	} else {
	        	Debug.Log("Button has no text component!");
	      	}
	    } else {
	      	Debug.Log("No sync button found!");
	    }
  	}

	public void ARFrameUpdated(UnityARCamera camera) {
		if (!lockPoints) {
			foreach (Vector3 point in camera.pointCloudData) {
				if (pointCloudQueue.Contains (point)) {
					continue;
				}

				if (pointCloudQueue.Count > POINTCLOUDBUFFERSIZE) {
					pointCloudQueue.Dequeue ();
				}

				pointCloudQueue.Enqueue (point);
			}
			pointClouds = pointCloudQueue.ToArray();
		}
	}

	void Update () {
		frames++;
		if (frames % 10 == 0) {
			SyncWorld ();
			frames = 0;
			if (pollForAnchor) {
				StartCoroutine(requestAnchorPoint ());
			}
		}

		if (setHeading) {
			Camera tempCam = mainCamera.GetComponentInChildren<Camera>();
			GameObject t = selectedMultiplayARAnchor;
			t.transform.LookAt(mainCamera.transform, -Vector3.up);
			Vector3 targetposition = tempCam.WorldToScreenPoint (t.transform.position);
			int blocksize = 50;
			if (targetposition.x < (Screen.width / 2) + blocksize && targetposition.x > (Screen.width / 2) - blocksize &&
				targetposition.y < (Screen.height / 2) + blocksize && targetposition.y > (Screen.height / 2) - blocksize) {

				setHeading = false;
				heading = Input.compass.trueHeading;
				var main = t.GetComponentInChildren<ParticleSystem> ().main;
				main.startColor = new Color (0, 100, 0, 1);
				t.transform.localScale = Vector3.one * 0.55f;

				anchorChosen = true;

				// generate a local cube to fuck with
				multiplayerObject = Instantiate (multiplayerObjectPrefabOne, new Vector3 (0, 0, 0), Quaternion.identity);
				multiplayerObject.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
				multiplayerObject.SetActive (true);
				// anchor.SetActive (false);

				TransformManager tm = multiplayerObject.AddComponent<TransformManager> ();
				tm.nm = this;
			}

		} else {

			if (!anchorSelect) {
				Camera tempCam = mainCamera.GetComponentInChildren<Camera> ();

				for (int i = 0; i < targets.Count; i++) {
					GameObject t = targets [i];
					t.transform.LookAt (mainCamera.transform, -Vector3.up);
					Vector3 targetposition = tempCam.WorldToScreenPoint (t.transform.position);
					int blocksize = 50;
					if (targetposition.x < (Screen.width / 2) + blocksize && targetposition.x > (Screen.width / 2) - blocksize &&
					    targetposition.y < (Screen.height / 2) + blocksize && targetposition.y > (Screen.height / 2) - blocksize) {

						var main = t.GetComponentInChildren<ParticleSystem> ().main;
						main.startColor = new Color (0, 100, 0, 1);
						targetenabled [i] = true;
						t.transform.localScale = Vector3.one * 0.55f;
						//					if (scaleEnumerator[i] == null) {
						//						scaleEnumerator[i] = ScaleObject (t, 1.0f, 0.3f, i);
						//						StartCoroutine (scaleEnumerator[i]);
						//					}
					} else {
						var main = t.GetComponentInChildren<ParticleSystem> ().main;
						main.startColor = ogColor;
						targetenabled [i] = false;
						t.transform.localScale = Vector3.one * 0.25f;
						//					if (scaleEnumerator[i] != null) {
						//						StopCoroutine (scaleEnumerator[i]);
						//						scaleEnumerator[i] = null;
						//					}
					}
				}

				// if the target is aligned with screen middle, change color to green
				if (Input.touchCount > 0 && Input.GetTouch (0).phase == TouchPhase.Began) {
					Ray ray = Camera.main.ScreenPointToRay (Input.GetTouch (0).position);
					RaycastHit hit;

					if (Physics.Raycast (ray, out hit)) {
						if (hit.transform.gameObject.name.StartsWith ("target")) {
							int index;
							int.TryParse (hit.transform.gameObject.name.Substring (6), out index);
							Debug.Log (hit.transform.gameObject.name.Substring (6));
							if (targetselected [index] && targetenabled [index]) {
								// deselect anchor
								// hit.transform.gameObject.GetComponent<SpriteRenderer> ().sprite = unselected;
								targetselected [index] = false;
							} else if (targetenabled [index]) {
								// select anchor
								// hit.transform.gameObject.GetComponent<SpriteRenderer> ().sprite = selected;
								anchor = hit.transform.gameObject;
								Debug.Log ("Anchor chosen!");
								SendSelectedAnchor2D ();


								for (int i = 0; i < targets.Count; i++) {
									GameObject other = targets [i];
									if (i != index) {
										StartCoroutine (ScaleObject (other, -1.0f, 0.0f, -1));
									}
								}
								targetselected [index] = true;
							}
						}
					}
				}
			}
		}
	}

  /**
  * Scales a given game object out of the scene over multiple frames.
  */
	private IEnumerator ScaleObject(GameObject obj, float direction, float end, int i) {
		bool stop = false;
		float speed = 0.75f;
	    while (!stop) {
			stop = direction == -1.0f ? obj.transform.localScale.x <= end : obj.transform.localScale.x >= end;
			Vector3 diff = (Vector3.one * Time.deltaTime * direction * speed);
		    obj.transform.localScale += diff;
	        yield return new WaitForSeconds (0.1f);
	    }
		// finally set the object itself to be inactive to save performance.
		if (i >= 0) {
			scaleEnumerator [i] = null;
		} else {
			obj.SetActive(false);
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

	public void testScreenshot() {
		Debug.Log ("Sending a new screenshot!");
		StartCoroutine (captureCameraView());
	}


  public void debugServerPoints(int height, int width) {
    Camera tempCam = mainCamera.GetComponentInChildren<Camera>();
		for (int i = 0; i < Mathf.Min (4, pointClouds.Length); i++) {
			Vector3 testPoint = pointClouds [i];
			Vector3 screenPos = tempCam.WorldToScreenPoint (testPoint);
			hitPointMap.Add (new Vector2(screenPos.x, screenPos.y), testPoint);
			reverseHitPointMap.Add (testPoint, new Vector2(screenPos.x, screenPos.y));
			PointInformation fakePoint = new PointInformation(screenPos.x, screenPos.y);
			Debug.Log ("making a fake point");
			drawServerPoint (fakePoint, i);
			anchorSelect = false;
		}
  }

	public IEnumerator captureCameraView() {
		int height = Screen.height;
		int width = Screen.width;

		// hide the yellow points!
		GameObject ps = GameObject.Find ("PointCloudParticleExample");
		ParticleSystem psp = ps.GetComponent<PointCloudParticleExample>().currentPS;
		if (psp != null) {
			Destroy (psp);
		}
		ps.SetActive(false);

	    // DEBUG code below that chooses the first 4 AR points and uses them to the sync up
	    // to the rest of the existing multiplayar system
		Debug.Log("Debug value: " + DEBUG);
		if (DEBUG == true) {
			debugServerPoints (height, width);
			yield break;
		} else {

			// create a texture to render the camera's view to
			RenderTexture texture = new RenderTexture (width, height, 24);

			// alternatively use textures from the main camera
			Camera cam = mainCamera.GetComponentInChildren<Camera> ();
			int prevMask = cam.cullingMask;
			cam.cullingMask = 0;
			RenderTexture prevTexture = cam.targetTexture;
			cam.targetTexture = texture;
			cam.Render ();

			// read the texture into a static texture2D
			RenderTexture.active = texture; // make the global render texture this one
			Texture2D savedTexture = new Texture2D (width, height, TextureFormat.RGB24, false);
			savedTexture.ReadPixels (new Rect (0, 0, width, height), 0, 0);
			savedTexture.Apply ();
			RenderTexture.active = null; // release the static reference

			// once done, restore the main camera
			cam.cullingMask = prevMask;
			cam.targetTexture = prevTexture;

			// send this image to a server
			Debug.Log ("Sending to the server");
			byte[] raw_image_bytes;
			raw_image_bytes = savedTexture.EncodeToPNG ();
			Debug.Log ("Writing " + raw_image_bytes.Length);
			Dictionary<string, string> headers = new Dictionary<string, string> ();
			headers.Add ("x-user-id", guid);
			headers.Add ("x-points", serializeARKitPoints ());

			WWW w = new WWW (address + imageEndpoint, raw_image_bytes, headers);
			yield return w;

			Debug.Log ("Response has returned");
			if (w.error != null) {
				Debug.Log (w.error);
			} else {
				Debug.Log ("IMAGE POINTS RETRIEVED FROM THE SERVER");
				MatchedPoints points = MatchedPoints.CreateFromJSON (w.text);
				Debug.Log (points.points.Count);

				if (points.points.Count == 0) {
					pollForAnchor = true;

				} else {
					int i = 0;
					foreach (PointInformation point in points.points) {
						Debug.Log ("POINT x: " + point.x + " y: " + point.y);
						drawServerPoint (point, i);
						i++;
					}
					anchorSelect = false;
				}
			}
		}
	}

	public IEnumerator requestAnchorPoint() {
		pollForAnchor = false;
		Dictionary<string, string> headers = new Dictionary<string, string> ();
		headers.Add ("x-user-id", guid);

		WWW w = new WWW (address + pointPollEndpoint, empty_post_data, headers);
		yield return w;

		if (w.error != null) {
			Debug.Log (w.error);
		} else {
			if (w.text.Length == 0) {
				pollForAnchor = true;
			} else {
				PointInformation anchorpoint = PointInformation.CreateFromJSON (w.text);

				selectedMultiplayARAnchor = drawServerPoint (anchorpoint, 0);

				// anchor selected on server! LOCK IN
				//anchorChosen = true;
				anchorSelect = true;
				anchor = selectedMultiplayARAnchor;
				setHeading = true;
			}
		}
		yield break;
	}

	public string serializeARKitPoints() {
		lockPoints = true;
		string serialized = "";
		Camera cam = mainCamera.GetComponentInChildren<Camera> ();
		Debug.Log ("Number of points we're sending: " + pointClouds.Length);
		foreach (Vector3 point in pointClouds) {
			Vector3 screenPos = cam.WorldToScreenPoint (point);
	      	string pointStringX = string.Format ("{0:0.000}", screenPos.x);
	      	string pointStringY = string.Format ("{0:0.000}", screenPos.y);
	      	string pointString = string.Format("{0},{1};", pointStringX, pointStringY);
			serialized += pointString;
			hitPointMap.Add (new Vector2(float.Parse(pointStringX), float.Parse(pointStringY)), point);
			reverseHitPointMap.Add (point, new Vector2(float.Parse(pointStringX), float.Parse(pointStringY)));
		}
		lockPoints = false;
		return serialized.Substring(0, serialized.Length - 1);
	}


	public GameObject drawServerPoint(PointInformation point, int index) {
		Vector2 pointVector = new Vector2 (point.x, point.y);
		Vector3 anchorposition;

		GameObject pointObj = null;

   		bool matched = false;
		foreach (KeyValuePair<Vector2, Vector3> val in hitPointMap) {
			Vector2 mapPoint = val.Key;
			if (mapPoint.Equals (pointVector)) {
				matched = true;
			}

			if (matched) {
				Debug.Log ("Found point!");
				anchorposition = val.Value;
				Debug.Log (anchorposition.ToString ());
				pointObj = DisplayAnchorPoint (anchorposition, index);
				break;
			}
		}

	    if (!matched) {
	        Debug.Log("No local match found for the server point");
	        Debug.Log(pointVector);
	    }

		return pointObj;
	}

  private GameObject DisplayAnchorPoint(Vector3 pointPosition, int index) {
    matchpoints.Add (pointPosition);
    GameObject target = (GameObject) Instantiate (electricOrbPrefab, pointPosition, Quaternion.Euler(90, 45, 0));
	ogColor = target.GetComponentInChildren<ParticleSystem> ().main.startColor;
    target.name = "target" + index;
    // SpriteRenderer r = target.AddComponent<SpriteRenderer> ();
    // r.sprite = unselected;
    targetselected.Add (false);
	targetenabled.Add (false);
	scaleEnumerator.Add (null);
    target.transform.position = pointPosition;
    target.transform.localScale = new Vector3 (0.25f, 0.25f, 0.25f);
    targets.Add (target);
    return target;
  }


	bool HitTestWithResultType (ARPoint point, ARHitTestResultType resultTypes)
	{
		List<ARHitTestResult> hitResults = UnityARSessionNativeInterface.GetARSessionNativeInterface ().HitTest (point, resultTypes);
		if (hitResults.Count > 0) {
			Debug.Log ("Got a hit");
			foreach (var hitResult in hitResults) {
//				m_HitTransform.position = UnityARMatrixOps.GetPosition (hitResult.worldTransform);
//				m_HitTransform.rotation = UnityARMatrixOps.GetRotation (hitResult.worldTransform);
				Vector3 position = UnityARMatrixOps.GetPosition (hitResult.worldTransform);
				GameObject target = DisplayAnchorPoint (position, 0);

				// anchor selected on server! LOCK IN
				anchorChosen = true;
				anchorSelect = true;
				anchor = target;

				return true;
			}
		}
		return false;
	}

	public void SendSelectedAnchor2D() {
		if (synced) {
			return;
		}

		float heading = Input.compass.trueHeading;
		Debug.Log ("Heading: " + heading.ToString ());
		Vector3 selectedPosition3D = anchor.transform.position;
		Vector2 selectedPosition2D;
		reverseHitPointMap.TryGetValue (selectedPosition3D, out selectedPosition2D);
		Dictionary<string, string> headers = new Dictionary<string, string> ();
		headers.Add ("x-user-id", guid);
		headers.Add ("x-xcord", selectedPosition2D.x.ToString());
		headers.Add ("x-ycord", selectedPosition2D.y.ToString());

		synced = false;

		WWW www = new WWW(address + anchorEndpoint, empty_post_data, headers);
		StartCoroutine(WaitForUserId(www));
	}

	/* Method is called if the user added an object to the environment
	 * OR if an object's position has changed. Not sending an ObjectId
	 * signals the server that this is a brand new object.
	 */
	public void SendObject(string objectId, GameObject obj) {
		GameObject temp = new GameObject ();
		temp.transform.position = obj.transform.position;
//		temp.transform.RotateAround (anchor.transform.position, Vector3.up, -1.0f * anchor.transform.rotation.eulerAngles.y);
		temp.transform.position = temp.transform.position - anchor.transform.position;
		Vector3 offset = temp.transform.position;

		Debug.Log ("Sending update for Object: " + objectId);
		Debug.Log ("Local Position: " + obj.transform.position + " World Position: " + offset.ToString());


		Dictionary<string, string> headers = new Dictionary<string, string> ();
		if (objectId.Length != 0) {
			headers.Add ("x-object-id", objectId);
		}
		headers.Add ("x-rotation", heading.ToString ());
		headers.Add ("x-xcord", offset.x.ToString ());
		headers.Add ("x-ycord", offset.y.ToString ());
		headers.Add ("x-zcord", offset.z.ToString ());

		WWW w = new WWW (address + objectEndpoint, empty_post_data, headers);
		StartCoroutine(WaitForObjectId(w, obj));
	}

	/* Get state of the world from server
	 */
	public void SyncWorld() {
		if (!anchorChosen) {
			return;
		}
    if (DEBUG == true) { return; } // spare server
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
		synced = true;
		Debug.Log ("received useless user id. Generating ROCKET");

		// generate a local cube to fuck with
		multiplayerObject = Instantiate (multiplayerObjectPrefabOne, new Vector3 (0, 0, 0), Quaternion.identity);
		multiplayerObject.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
		multiplayerObject.SetActive (true);
		// anchor.SetActive (false);

		TransformManager t = multiplayerObject.AddComponent<TransformManager> ();
		t.nm = this;

		// NOW start sending sync
		anchorChosen = true;
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
		TransformManager t = multiplayerObject.GetComponent<TransformManager> ();
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

				Debug.Log ("World Sync Object: " + objectId + " to position: " + otherPos.ToString ());

				GameObject temp = new GameObject ();
				temp.transform.position = otherPos + anchor.transform.position;
//				temp.transform.RotateAround (anchor.transform.position, Vector3.up, anchor.transform.rotation.eulerAngles.y);
				Vector3 newPos = temp.transform.position;

				obj.transform.position = newPos;
			} else {
        		// create a new game object!
				GameObject other = Instantiate (multiplayerObjectPrefabTwo, otherPos + anchor.transform.position, Quaternion.identity);
       			other.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
				float rotationOffset = heading - root.rotation;

        		other.transform.RotateAround (anchor.transform.position, Vector3.up, rotationOffset);

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
