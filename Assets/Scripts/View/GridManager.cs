using UnityEngine;

namespace MasterHouse
{
    public class GridManager : MonoBehaviour
    {
        public GameObject gridPrefab;
        
        private Transform gridParent=null;


        public void UpdateGridGO()
        {
            if (gridParent == null)
            {
                gridParent = transform.Find("GridGroup");
                if (gridParent == null)
                {
                    GameObject gridParentGO = new GameObject("GridGroup");
                    gridParentGO.transform.SetParent(transform,false);
                    gridParentGO.transform.localPosition = Vector3.zero;
                }
                
                int childCount = gridParent.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    gridParent.GetChild(i).gameObject.SetActive(true);
                }
            }
        }
        
    }
}