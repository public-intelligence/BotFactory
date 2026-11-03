import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import {
  PageShellComponent,

  LayoutService,
} from '@public-intelligence/ui-core';

import { Api } from '../../api/api';
import { createBot } from '../../api/functions';
import { resolveProductUrl } from '../../core/product-url';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'bf-bot-create',
  imports: [RouterLink, ReactiveFormsModule, PageShellComponent],
  template: `
    <app-page-shell title="Crear Bot">
      <nav breadcrumbs>
        <ol class="flex items-center gap-1 text-sm text-neutral-500">
          <li><a routerLink="/dashboard" class="hover:text-neutral-900">Dashboard</a></li>
          <li class="text-neutral-300">›</li>
          <li class="text-neutral-900">Crear Bot</li>
        </ol>
      </nav>

      <div class="mx-auto max-w-2xl">
        <form [formGroup]="form" (ngSubmit)="onSubmit()" class="space-y-6">
          <!-- Nombre del bot -->
          <div>
            <label class="mb-1 block text-sm font-medium text-neutral-900"
              >Nombre del bot</label
            >
            <input
              type="text"
              formControlName="botName"
              placeholder="Ej: Asistente de Soporte"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
            />
          </div>

          <!-- URL Slug -->
          <div>
            <label class="mb-1 block text-sm font-medium text-neutral-900"
              >URL Slug</label
            >
            <input
              type="text"
              formControlName="urlSlug"
              placeholder="asistente-soporte"
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
            />
            <p class="mt-1 text-xs text-neutral-400">
              URL: {{ productUrl }}/chat/{{ form.controls.urlSlug.value }}
            </p>
          </div>

          <!-- Prompt del Sistema -->
          <div>
            <label class="mb-1 block text-sm font-medium text-neutral-900"
              >Prompt del Sistema</label
            >
            <textarea
              rows="6"
              formControlName="masterPrompt"
              placeholder="Eres un asistente virtual amable y profesional..."
              class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none"
            ></textarea>
            <p class="mt-1 text-xs text-neutral-400">
              Define las instrucciones al comportamiento del bot.
            </p>
          </div>

              <!-- Actions -->
          <div class="flex items-center justify-end gap-3 border-t border-neutral-100 pt-6">
            <a
              routerLink="/dashboard"
              class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:text-neutral-900"
            >
              Cancelar
            </a>
            <button
              type="submit"
              [disabled]="submitting() || form.invalid"
              class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
            >
              {{ submitting() ? 'Creando...' : 'Crear Bot' }}
            </button>
          </div>
        </form>
      </div>
    </app-page-shell>
  `,
})
export class BotCreate implements OnInit {
  private readonly layout = inject(LayoutService);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly submitting = signal(false);
  readonly productUrl = resolveProductUrl();

  readonly form = this.fb.nonNullable.group({
    botName: ['', Validators.required],
    urlSlug: [''],
    masterPrompt: [''],
  });

  private slugManuallyEdited = false;

  ngOnInit() {
    this.layout.setPageTitle('Crear Bot');

    this.form.get('botName')!.valueChanges.subscribe(name => {
      if (!this.slugManuallyEdited) {
        const slug = name
          .toLowerCase()
          .normalize('NFD')
          .replace(/[\u0300-\u036f]/g, '')
          .replace(/[^a-z0-9\s-]/g, '')
          .replace(/\s+/g, '-')
          .replace(/-+/g, '-')
          .trim();
        this.form.get('urlSlug')!.setValue(slug, { emitEvent: false });
      }
    });

    this.form.get('urlSlug')!.valueChanges.subscribe(() => {
      this.slugManuallyEdited = true;
    });
  }

  async onSubmit() {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);

    try {
      const result = await this.api.invoke(createBot, {
        body: this.form.getRawValue(),
      });
      toast.success('Bot creado correctamente.');
      await this.router.navigate(['/bots', result.id, 'edit']);
    } catch {
      toast.error('Error al crear el bot. Intenta de nuevo.');
    } finally {
      this.submitting.set(false);
    }
  }
}
