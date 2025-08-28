using ABC_Retail_System.Models;
using ABC_Retail_System.Services;
using ABC_Retail_System.Services.Storage;
using Azure;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ABC_Retail_System.Controllers
{
    public class CustomerController : Controller
    {
        private readonly CustomerService _customerService;
        private readonly TableStorageService _tableStorage;
        private readonly QueueStorageService _queueStorage;

        public CustomerController(CustomerService customerService, TableStorageService tableStorage, QueueStorageService queueStorage)
        {
            _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
            _tableStorage = tableStorage ?? throw new ArgumentNullException(nameof(tableStorage));
            _queueStorage = queueStorage ?? throw new ArgumentNullException(nameof(queueStorage));
        }

        // GET: Customer/Index (List all customers)
        public async Task<IActionResult> Index()
        {
            var customers = await _customerService.GetAllAsync();
            return View(customers);
        }

        // GET: Customer/Details/5
        [HttpGet]
        [Route("Customer/Details/{id}")]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Customer ID is required.");
            }

            try
            {
                var customer = await _customerService.GetByRowKeyAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error retrieving customer: {ex.Message}");
                return View();
            }
        }

        // GET: Customer/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Customer/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FirstName,LastName,Email,PhoneNumber,Address")] Customer customer)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Create a new Customer instance with required ITableEntity properties
                    var newCustomer = new Customer
                    {
                        // Set form values
                        FirstName = customer.FirstName,
                        LastName = customer.LastName,
                        Email = customer.Email,
                        PhoneNumber = customer.PhoneNumber,
                        Address = customer.Address,
                        
                        // Set required ITableEntity properties
                        PartitionKey = "CUSTOMER",
                        RowKey = Guid.NewGuid().ToString("N"),
                        Timestamp = DateTimeOffset.UtcNow,
                        ETag = ETag.All
                    };

                    await _tableStorage.AddEntityAsync(newCustomer);
                    
                    // Log the action
                    try
                    {
                        await _queueStorage.SendLogEntryAsync(new 
                        { 
                            Action = "Customer Created",
                            CustomerId = newCustomer.RowKey,
                            CustomerName = $"{newCustomer.FirstName} {newCustomer.LastName}",
                            Timestamp = DateTime.UtcNow.ToString("o")
                        });
                    }
                    catch (Exception logEx)
                    {
                        // Log the error but don't fail the operation
                        Console.WriteLine($"Error logging customer creation: {logEx}");
                    }

                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, "An error occurred while creating the customer. Please try again.");
                    Console.WriteLine($"Error creating customer: {ex}");
                }
            }
            return View(customer);
        }

        // GET: Customer/Edit/5
        [HttpGet]
        [Route("Customer/Edit/{id}")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Customer ID is required.");
            }

            try
            {
                var customer = await _customerService.GetByRowKeyAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error retrieving customer: {ex.Message}");
                return View();
            }
        }

        // POST: Customer/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Customer/Edit/{id}")]
        public async Task<IActionResult> Edit(string id, [Bind("RowKey,FirstName,LastName,Email,PhoneNumber,Address,ETag")] Customer customerModel)
        {
            if (id != customerModel.RowKey)
            {
                return BadRequest("Customer ID mismatch.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Get the existing customer to preserve required properties
                    var existingCustomer = await _customerService.GetByRowKeyAsync(id);
                    if (existingCustomer == null)
                    {
                        return NotFound();
                    }

                    // Update only the editable properties
                    existingCustomer.FirstName = customerModel.FirstName;
                    existingCustomer.LastName = customerModel.LastName;
                    existingCustomer.Email = customerModel.Email;
                    existingCustomer.PhoneNumber = customerModel.PhoneNumber;
                    existingCustomer.Address = customerModel.Address;
                    existingCustomer.Timestamp = DateTimeOffset.UtcNow;
                    
                    await _customerService.UpdateAsync(existingCustomer);
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, "An error occurred while updating the customer. Please try again.");
                    Console.WriteLine($"Error updating customer: {ex}");
                }
            }
            return View(customerModel);
        }

        // GET: Customer/Delete/5
        [HttpGet]
        [Route("Customer/Delete/{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Customer ID is required.");
            }

            try
            {
                var customer = await _customerService.GetByRowKeyAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }
                return View(customer);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error retrieving customer: {ex.Message}");
                return View();
            }
        }

        // POST: Customer/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Route("Customer/Delete/{id}")]
        public async Task<IActionResult> DeleteConfirmed(string id, string eTag = null)
        {
            try
            {
                // Get the customer first to ensure it exists and to get the ETag
                var customer = await _customerService.GetByRowKeyAsync(id);
                if (customer == null)
                {
                    return NotFound();
                }

                // If eTag was provided in the form, use it (this comes from the hidden field in the Delete view)
                if (!string.IsNullOrEmpty(eTag))
                {
                    customer.ETag = new Azure.ETag(eTag);
                }

                // Delete the customer
                var result = await _customerService.DeleteAsync(id);
                if (!result)
                {
                    ModelState.AddModelError(string.Empty, "Failed to delete the customer. The customer may have been modified or deleted by another user.");
                    return View("Delete", customer);
                }

                TempData["SuccessMessage"] = "Customer deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (RequestFailedException ex) when (ex.Status == 412) // Precondition Failed
            {
                // Handle concurrency conflicts
                ModelState.AddModelError(string.Empty, "This customer was modified by another user. Please refresh and try again.");
                var customer = await _customerService.GetByRowKeyAsync(id);
                return View("Delete", customer ?? new Customer { RowKey = id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"An error occurred while deleting the customer: {ex.Message}");
                Console.WriteLine($"Error deleting customer: {ex}");
                var customer = await _customerService.GetByRowKeyAsync(id);
                return View("Delete", customer ?? new Customer { RowKey = id });
            }
        }
    }
}