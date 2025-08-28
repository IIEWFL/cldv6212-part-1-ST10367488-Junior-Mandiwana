using ABC_Retail_System.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ABC_Retail_System.Controllers
{
    public class QueueLogsController : Controller
    {
        private readonly FileShareStorageService _fileShareService;
        private const string QueueName = "abcretail-queue";

        public QueueLogsController(FileShareStorageService fileShareService)
        {
            _fileShareService = fileShareService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var messages = await _fileShareService.GetQueueMessagesAsync(QueueName);
                return View(messages);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error retrieving queue logs: {ex.Message}";
                return View(new List<string>());
            }
        }
    }
}
