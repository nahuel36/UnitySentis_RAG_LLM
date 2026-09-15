// ChatDispatcher.cs
using UnityEngine;

namespace ChatSystemWithSentis
{
    class ChatDispatcher : MonoBehaviour
    {
        [SerializeField] private AdventureQueryEngine engine;
        [SerializeField] private TMPro.TMP_InputField inputField;
        [SerializeField] private TMPro.TextMeshProUGUI response;
        public void OnPlayerSubmit()
        {
            response.text = "Pensando...";
            StartCoroutine(engine.QueryCoroutine(inputField.text, OnQueryComplete));
        }

        private void OnQueryComplete(QueryResult result)
        {
            if (!result.HasMatch)
            {
                response.text = "No tengo información al respecto.";
                return;
            }

            response.text = result.Entry.responseText;

            //foreach (var unlock in result.Entry.unlocks)
              //  GameEvents.Dispatch(unlock);
        }
    }
}
