import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NgxSonnerToaster } from 'ngx-sonner';

@Component({
  selector: 'bf-root',
  imports: [RouterOutlet, NgxSonnerToaster],
  template: `<ngx-sonner-toaster [position]="'bottom-right'" [richColors]="true" /><router-outlet />`,
  styleUrl: './app.css',
})
export class App {}
