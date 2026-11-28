/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 *
 * MOAVI Desk's bar on FreeRDP's Android session screen, as on the Windows and macOS windows: the desktop's name,
 * 장치, 키보드, 한/영, Ctrl+Alt+Del, 연결 끊기. installer/android/build-freerdp.sh adds this file to freeRDPCore
 * and hooks it into SessionActivity (MoaviBar.attach after the views are set up). The labels and the device
 * choices come from MOAVI Desk in the intent (moavi.title, moavi.labels, moavi.devlabels, moavi.allow, moavi.on);
 * without them (FreeRDP opened some other way) there is no bar. 장치 switches the phone's storage (a drive, through
 * moavi_android.c and the device channel) and the clipboard during the session, as far as the pool allows them.
 */
package com.freerdp.freerdpcore.presentation;

import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.RelativeLayout;
import android.widget.TextView;

import com.freerdp.freerdpcore.R;
import com.freerdp.freerdpcore.services.LibFreeRDP;

public final class MoaviBar
{
	private static final int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_DELETE = 0x2E, VK_HANGUL = 0x15;
	private static final int PHONE_DRIVE = 1;

	/** The 장치 menu's clipboard switch (SessionActivity checks it both ways). */
	public static volatile boolean clipboardOn = true;
	private static boolean driveOn;

	/** moavi_android.c: the phone's storage as a drive of the session (instance 0 = the one about to connect). */
	static native void drive(long instance, int token, String name, String path, boolean on);

	private MoaviBar()
	{
	}

	public static void attach(final SessionActivity activity)
	{
		Intent intent = activity.getIntent();
		String title = intent.getStringExtra("moavi.title");
		String[] labels = intent.getStringArrayExtra("moavi.labels");
		final String[] devLabels = intent.getStringArrayExtra("moavi.devlabels");
		final boolean[] allow = intent.getBooleanArrayExtra("moavi.allow");
		final boolean[] on = intent.getBooleanArrayExtra("moavi.on");
		if (title == null || labels == null || labels.length < 5 || devLabels == null || devLabels.length < 3 || allow == null ||
		    allow.length < 2 || on == null || on.length < 2)
			return;

		// what this connection starts with: the phone's storage waits for the device channel
		final String storage = android.os.Environment.getExternalStorageDirectory().getPath();
		clipboardOn = allow[1] && on[1];
		driveOn = allow[0] && on[0];
		if (driveOn)
			drive(0, PHONE_DRIVE, "Phone", storage, true);

		RelativeLayout root = activity.findViewById(R.id.session_root_view);
		View scroll = activity.findViewById(R.id.sessionScrollView);
		if (root == null || scroll == null)
			return;

		LinearLayout bar = new LinearLayout(activity);
		bar.setId(View.generateViewId());
		bar.setOrientation(LinearLayout.HORIZONTAL);
		bar.setGravity(Gravity.CENTER_VERTICAL);
		bar.setBackgroundColor(Color.parseColor("#111827"));
		int pad = dp(activity, 6);
		bar.setPadding(dp(activity, 12), pad / 2, pad, pad / 2);

		TextView name = new TextView(activity);
		name.setText(title);
		name.setTextColor(Color.WHITE);
		name.setTypeface(Typeface.DEFAULT_BOLD);
		name.setSingleLine(true);
		name.setEllipsize(android.text.TextUtils.TruncateAt.END);
		// the name: at most a third of the screen; the buttons get the rest and scroll when they need more
		name.setMaxWidth(activity.getResources().getDisplayMetrics().widthPixels / 3);
		bar.addView(name, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));

		// the buttons scroll sideways on a narrow screen; 연결 끊기 stays at the right edge
		LinearLayout buttons = new LinearLayout(activity);
		buttons.setOrientation(LinearLayout.HORIZONTAL);
		HorizontalScrollView scroller = new HorizontalScrollView(activity);
		scroller.setHorizontalScrollBarEnabled(false);
		scroller.addView(buttons);
		buttons.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
		scroller.setFillViewport(true);
		LinearLayout.LayoutParams sp0 = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
		bar.addView(scroller, sp0);

		final Button devicesButton = button(activity, labels[0]);
		devicesButton.setOnClickListener(v -> {
			PopupMenu menu = new PopupMenu(activity, devicesButton);
			android.view.MenuItem phone = menu.getMenu().add(0, 1, 0, devLabels[0]);
			phone.setCheckable(true).setChecked(driveOn).setEnabled(allow[0]);
			android.view.MenuItem clip = menu.getMenu().add(0, 2, 1, devLabels[1]);
			clip.setCheckable(true).setChecked(clipboardOn).setEnabled(allow[1]);
			if (!allow[0] || !allow[1])
				menu.getMenu().add(0, 3, 2, devLabels[2]).setEnabled(false);
			menu.setOnMenuItemClickListener(item -> {
				if (item.getItemId() == 1)
				{
					driveOn = !driveOn;
					drive(activity.moaviInstance(), PHONE_DRIVE, "Phone", storage, driveOn);
				}
				else if (item.getItemId() == 2)
					clipboardOn = !clipboardOn;
				return true;
			});
			menu.show();
		});
		buttons.addView(devicesButton);

		Button keyboard = button(activity, labels[1]);
		keyboard.setOnClickListener(v -> activity.moaviToggleKeyboard());
		buttons.addView(keyboard);

		Button hangul = button(activity, labels[2]);
		hangul.setOnClickListener(v -> {
			long inst = activity.moaviInstance();
			if (inst == 0)
				return;
			LibFreeRDP.sendKeyEvent(inst, VK_HANGUL, true);
			LibFreeRDP.sendKeyEvent(inst, VK_HANGUL, false);
		});
		buttons.addView(hangul);

		Button cad = button(activity, labels[3]);
		cad.setOnClickListener(v -> {
			long inst = activity.moaviInstance();
			if (inst == 0)
				return;
			LibFreeRDP.sendKeyEvent(inst, VK_CONTROL, true);
			LibFreeRDP.sendKeyEvent(inst, VK_MENU, true);
			LibFreeRDP.sendKeyEvent(inst, VK_DELETE, true);
			LibFreeRDP.sendKeyEvent(inst, VK_DELETE, false);
			LibFreeRDP.sendKeyEvent(inst, VK_MENU, false);
			LibFreeRDP.sendKeyEvent(inst, VK_CONTROL, false);
		});
		buttons.addView(cad);

		Button disconnect = button(activity, labels[4]);
		disconnect.setBackgroundColor(Color.parseColor("#0E7C6B"));
		disconnect.setOnClickListener(v -> {
			long inst = activity.moaviInstance();
			if (inst != 0)
				LibFreeRDP.disconnect(inst);
			activity.finish();
		});
		bar.addView(disconnect);

		RelativeLayout.LayoutParams barParams =
		    new RelativeLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
		barParams.addRule(RelativeLayout.ALIGN_PARENT_TOP);
		root.addView(bar, barParams);

		// the desktop below the bar, not under it
		RelativeLayout.LayoutParams sp = (RelativeLayout.LayoutParams)scroll.getLayoutParams();
		sp.removeRule(RelativeLayout.ALIGN_PARENT_TOP);
		sp.addRule(RelativeLayout.BELOW, bar.getId());
		scroll.setLayoutParams(sp);
	}

	private static Button button(SessionActivity activity, String text)
	{
		Button b = new Button(activity, null, android.R.attr.borderlessButtonStyle);
		b.setText(text);
		b.setAllCaps(false);
		b.setTextColor(Color.WHITE);
		b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
		b.setMinWidth(0);
		b.setMinimumWidth(0);
		b.setPadding(dp(activity, 10), 0, dp(activity, 10), 0);
		return b;
	}

	private static int dp(SessionActivity a, int v)
	{
		return Math.round(v * a.getResources().getDisplayMetrics().density);
	}
}
