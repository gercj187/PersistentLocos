using DV;
using UnityEngine;
using System.Collections;

namespace PersistentLocos
{
    public class CoroutineDispatcher : MonoBehaviour
    {
        private static CoroutineDispatcher _instance;

        public static CoroutineDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("PersistentLocos_CoroutineDispatcher");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineDispatcher>();
                }
                return _instance;
            }
        }

        public void RunCoroutine(IEnumerator coroutine)
        {
            StartCoroutine(coroutine);
        }
    }
}
