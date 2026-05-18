(() => {
  document.querySelectorAll('.clickable-card[data-href]').forEach((card) => {
    const openCard = () => {
      const href = card.getAttribute('data-href');
      if (href) {
        window.location.href = href;
      }
    };

    card.addEventListener('click', (event) => {
      if (event.target.closest('a, button, input, select, textarea, [data-card-ignore]')) {
        return;
      }

      openCard();
    });

    card.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter' && event.key !== ' ') {
        return;
      }

      event.preventDefault();
      openCard();
    });
  });

  const modalElement = document.getElementById('imagePreviewModal');
  const modalImage = document.getElementById('imagePreviewModalImage');
  const modalTitle = document.getElementById('imagePreviewModalTitle');

  if (!modalElement || !modalImage || !modalTitle || !window.bootstrap) {
    return;
  }

  const modal = new bootstrap.Modal(modalElement);

  document.querySelectorAll('.image-modal-trigger[data-image-src]').forEach((trigger) => {
    trigger.addEventListener('click', () => {
      const src = trigger.getAttribute('data-image-src');
      const title = trigger.getAttribute('data-image-title') || 'Screenshot';

      if (!src) {
        return;
      }

      modalImage.src = src;
      modalImage.alt = title;
      modalTitle.textContent = title;
      modal.show();
    });
  });

  modalElement.addEventListener('hidden.bs.modal', () => {
    modalImage.src = '';
    modalImage.alt = '';
  });
})();
